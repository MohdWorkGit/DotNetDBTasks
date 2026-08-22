using System.Diagnostics;
using System.Text.Json;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;
using Bayan.Application.Common.Reporting;
using Bayan.Application.Common.Security;
using Bayan.Application.Features.QueryExecution.Commands;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Reports.Commands;

/// <summary>
/// Runs a report: every dataset, once, with the report's shared parameters routed to each
/// underlying query.
/// </summary>
public class RunReportCommand : IRequest<ReportRunResult>
{
    public Guid ReportId { get; set; }

    /// <summary>
    /// The report-level parameter values as the form submitted them. These are routed to the
    /// dataset queries as raw strings and typed there, against each query's own parameter
    /// metadata — the report form decides what is <i>asked</i>, the query decides what a value
    /// <i>means</i>.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = new();
}

/// <summary>
/// The report orchestrator.
///
/// <para>Every dataset is executed by sending an <see cref="ExecuteQueryCommand"/> through
/// MediatR rather than touching <c>IQueryExecutor</c> directly. That is the whole design: it is
/// the single execution funnel, so per-query access checks, database-user resolution and
/// password decryption, typed parameter coercion, multi-value IN-clause expansion, row caps and
/// one <c>QueryExecutionLog</c> row per dataset all come for free and stay identical to an
/// interactive run. In particular it means a caller who lacks access to one of the queries is
/// refused <i>by that query</i>, so a report cannot become a way to reach data they could not
/// reach directly.</para>
/// </summary>
public class RunReportCommandHandler : IRequestHandler<RunReportCommand, ReportRunResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;

    public RunReportCommandHandler(
        IUnitOfWork unitOfWork,
        IMediator mediator,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mediator = mediator;
        _currentUser = currentUser;
    }

    public async Task<ReportRunResult> Handle(RunReportCommand request, CancellationToken cancellationToken)
    {
        var report = await _unitOfWork.Reports.GetByIdAsync(
            request.ReportId, cancellationToken,
            "Datasets", "Datasets.ParameterMaps", "Datasets.ParameterMaps.ReportParameter",
            "Parameters", "Charts");
        if (report is null)
            throw new NotFoundException(nameof(Report), request.ReportId);

        if (!report.IsEnabled)
            throw new DomainException("This report is currently disabled.");

        await ReportAccess.EnsureCanRunAsync(report, _unitOfWork, _currentUser, cancellationToken);

        var reportValues = ResolveReportParameters(report, request.Parameters);

        // The report's timeout bounds the whole run, not each dataset. A report fans out to
        // several queries, so per-query timeouts alone would let it run for the sum of its parts.
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, report.TimeoutSeconds)));

        var result = new ReportRunResult
        {
            ReportId = report.Id,
            ReportName = report.Name,
            Parameters = BuildExportParameters(report, reportValues)
        };

        var stopwatch = Stopwatch.StartNew();
        var rowBudget = report.MaxTotalRows;

        // Inputs before whatever consumes them: a detail needs its parent's rows, a join needs
        // both of its sides.
        var datasets = OrderByDependency(report.Datasets
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.DatasetKey)
            .ToList());

        var sectionsByDatasetId = new Dictionary<Guid, ReportSectionResult>();

        foreach (var dataset in datasets)
        {
            var section = new ReportSectionResult
            {
                Key = dataset.DatasetKey,
                Title = string.IsNullOrWhiteSpace(dataset.DisplayName) ? dataset.DatasetKey : dataset.DisplayName,
                DynamicQueryId = dataset.DynamicQueryId,
                IsVisibleInViewer = dataset.IsVisibleInViewer
            };

            // A join draws its rows from two other datasets, so it is the one kind that has no
            // query of its own.
            if (dataset.SourceType != ReportDatasetSourceType.Join && dataset.DynamicQueryId is null)
            {
                section.Error = "This dataset has no query assigned.";
                sectionsByDatasetId[dataset.Id] = section;
                result.Sections.Add(section);
                continue;
            }

            try
            {
                runCts.Token.ThrowIfCancellationRequested();

                if (dataset.SourceType == ReportDatasetSourceType.Join)
                {
                    EvaluateJoin(dataset, section, sectionsByDatasetId);
                }
                else if (dataset.SourceType == ReportDatasetSourceType.Detail)
                {
                    await ExpandDetailAsync(
                        report, dataset, section, sectionsByDatasetId, reportValues, result, runCts.Token);
                }
                else
                {
                    // Not null here: a non-join dataset without a query was turned into a failed
                    // section above, before this try block.
                    var queryId = dataset.DynamicQueryId
                        ?? throw new InvalidOperationException(
                            $"Dataset '{dataset.DatasetKey}' reached execution with no query.");

                    var execution = await _mediator.Send(new ExecuteQueryCommand
                    {
                        QueryId = queryId,
                        Parameters = BuildQueryParameters(dataset, reportValues, parentRow: null),
                        // Cache the complete result set so the viewer can page it and the export
                        // can reuse it, from one execution.
                        CacheFullResult = true
                    }, runCts.Token);

                    section.Columns = execution.Columns;
                    section.Rows = execution.Rows;
                    section.TotalRows = execution.TotalRows;
                    section.ExecutionDurationMs = execution.ExecutionDurationMs;
                }

                rowBudget -= section.Rows.Count;
                if (rowBudget < 0)
                {
                    // Datasets run uncapped so no single section is silently truncated, which
                    // means the report as a whole needs its own ceiling.
                    result.Warnings.Add(
                        $"The report reached its {report.MaxTotalRows:N0} row limit at section '{section.Title}'. " +
                        "Later sections were not run.");
                    sectionsByDatasetId[dataset.Id] = section;
                    result.Sections.Add(section);
                    break;
                }
            }
            catch (OperationCanceledException) when (runCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                section.Error = $"The report exceeded its {report.TimeoutSeconds}s time limit.";
                sectionsByDatasetId[dataset.Id] = section;
                result.Sections.Add(section);
                result.Warnings.Add("The run was stopped at its time limit; later sections were not run.");
                break;
            }
            catch (OperationCanceledException)
            {
                throw; // the caller cancelled — not a section failure
            }
            catch (Exception ex) when (ex is DomainException or ForbiddenAccessException or NotFoundException or QueryTimeoutException)
            {
                // One dataset failing loses that section, not the document. The reason is
                // carried on the section so the reader sees why it is missing rather than
                // wondering whether the data was simply empty.
                section.Error = ex.Message;
            }

            sectionsByDatasetId[dataset.Id] = section;
            result.Sections.Add(section);
        }

        BuildCharts(report, sectionsByDatasetId, result);

        stopwatch.Stop();
        result.ExecutionDurationMs = stopwatch.ElapsedMilliseconds;

        await WriteRunLogAsync(report, request.Parameters, result, cancellationToken);
        return result;
    }

    /// <summary>
    /// Builds every chart the report defines, from the sections that have just run.
    ///
    /// <para>A chart that cannot be drawn — its dataset failed, its column is gone, its series
    /// list is empty — is left out with a warning rather than rendered as empty axes. An empty
    /// plot looks like a finding; a warning reads as a problem with the report.</para>
    /// </summary>
    private static void BuildCharts(
        Report report,
        IReadOnlyDictionary<Guid, ReportSectionResult> sectionsByDatasetId,
        ReportRunResult result)
    {
        foreach (var chart in report.Charts.OrderBy(c => c.SortOrder).ThenBy(c => c.ChartKey))
        {
            if (!sectionsByDatasetId.TryGetValue(chart.DatasetId, out var section))
            {
                result.Warnings.Add($"Chart '{chart.ChartKey}' reads from a dataset that did not run.");
                continue;
            }

            var data = ReportChartBuilder.Build(chart, section);
            if (data is null)
            {
                result.Warnings.Add(
                    $"Chart '{chart.ChartKey}' could not be drawn from '{section.Title}' — " +
                    "check its category and series columns still exist.");
                continue;
            }

            result.Charts.Add(data);
        }
    }

    /// <summary>
    /// Orders datasets so everything an item reads from runs first: a detail after its parent,
    /// a join after both of its sides. Repeatedly take whatever has no unsatisfied input left.
    /// Anything still unplaced is part of a cycle and is appended so it fails with its own
    /// message rather than disappearing silently.
    /// </summary>
    private static List<ReportDataset> OrderByDependency(List<ReportDataset> datasets)
    {
        var ordered = new List<ReportDataset>();
        var placed = new HashSet<Guid>();
        var remaining = new List<ReportDataset>(datasets);

        while (remaining.Count > 0)
        {
            var ready = remaining.Where(IsReady).ToList();

            bool IsReady(ReportDataset d) =>
                (d.ParentDatasetId is null || placed.Contains(d.ParentDatasetId.Value))
                && (d.LeftDatasetId is null || placed.Contains(d.LeftDatasetId.Value))
                && (d.RightDatasetId is null || placed.Contains(d.RightDatasetId.Value));

            if (ready.Count == 0)
            {
                ordered.AddRange(remaining);
                break;
            }

            foreach (var dataset in ready)
            {
                ordered.Add(dataset);
                placed.Add(dataset.Id);
                remaining.Remove(dataset);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Builds a join dataset from two sections that have already run.
    ///
    /// <para>Both sides are in memory by this point, which is the whole reason the join happens
    /// here rather than in SQL: the two datasets are separate saved queries and may not even
    /// run against the same database connection.</para>
    /// </summary>
    private static void EvaluateJoin(
        ReportDataset dataset,
        ReportSectionResult section,
        IReadOnlyDictionary<Guid, ReportSectionResult> sectionsByDatasetId)
    {
        if (dataset.LeftDatasetId is null || dataset.RightDatasetId is null ||
            string.IsNullOrWhiteSpace(dataset.LeftColumn) || string.IsNullOrWhiteSpace(dataset.RightColumn))
        {
            section.Error = "This join is missing one of its two datasets or its matching columns.";
            return;
        }

        if (!sectionsByDatasetId.TryGetValue(dataset.LeftDatasetId.Value, out var left) ||
            !sectionsByDatasetId.TryGetValue(dataset.RightDatasetId.Value, out var right))
        {
            section.Error = "One of the datasets this join reads from did not run.";
            return;
        }

        if (!left.IsSuccess || !right.IsSuccess)
        {
            var failed = left.IsSuccess ? right.Title : left.Title;
            section.Error = $"The dataset '{failed}' did not run, so this join has nothing to match.";
            return;
        }

        // A column that does not exist would otherwise match nothing and look like "no data",
        // which is the hardest kind of report problem to diagnose.
        if (!left.Columns.Contains(dataset.LeftColumn, StringComparer.OrdinalIgnoreCase))
        {
            section.Error = $"'{left.Title}' has no column named '{dataset.LeftColumn}'.";
            return;
        }

        if (!right.Columns.Contains(dataset.RightColumn, StringComparer.OrdinalIgnoreCase))
        {
            section.Error = $"'{right.Title}' has no column named '{dataset.RightColumn}'.";
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        var joined = ReportJoinEvaluator.Join(
            left.Columns,
            left.Rows.Cast<IReadOnlyDictionary<string, object?>>().ToList(),
            right.Columns,
            right.Rows.Cast<IReadOnlyDictionary<string, object?>>().ToList(),
            dataset.LeftColumn,
            dataset.RightColumn,
            dataset.JoinType ?? ReportJoinType.Inner,
            rightKeyPrefix: right.Key);

        stopwatch.Stop();

        section.Columns = joined.Columns;
        section.Rows = joined.Rows;
        section.TotalRows = joined.Rows.Count;
        section.ExecutionDurationMs = stopwatch.ElapsedMilliseconds;
    }

    /// <summary>
    /// Runs a detail dataset once per row of its parent -- the "for each customer, their
    /// purchases" shape -- and tags every returned row with the index of the parent row it came
    /// from, so the Word template can render each parent's rows under that parent.
    ///
    /// <para>This is N+1 by construction, which is why it is bounded three ways: the parent row
    /// cap, the report's overall time limit through <paramref name="cancellationToken"/>, and
    /// the row budget applied by the caller. Reaching the cap is reported as a warning rather
    /// than silently truncating the document.</para>
    /// </summary>
    private async Task ExpandDetailAsync(
        Report report,
        ReportDataset dataset,
        ReportSectionResult section,
        IReadOnlyDictionary<Guid, ReportSectionResult> sectionsByDatasetId,
        IReadOnlyDictionary<string, string> reportValues,
        ReportRunResult result,
        CancellationToken cancellationToken)
    {
        if (dataset.ParentDatasetId is null ||
            !sectionsByDatasetId.TryGetValue(dataset.ParentDatasetId.Value, out var parent))
        {
            section.Error = "This dataset has no parent section, or the parent did not run.";
            return;
        }

        if (!parent.IsSuccess)
        {
            section.Error = $"The parent section '{parent.Title}' did not run.";
            return;
        }

        var cap = Math.Min(parent.Rows.Count, Math.Max(1, report.MaxDetailRows));
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < cap; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var execution = await _mediator.Send(new ExecuteQueryCommand
            {
                QueryId = dataset.DynamicQueryId!.Value,
                Parameters = BuildQueryParameters(dataset, reportValues, parent.Rows[index]),
                CacheFullResult = true
            }, cancellationToken);

            // Every child run returns the same shape, so the first one settles the columns.
            if (section.Columns.Count == 0)
                section.Columns = execution.Columns;

            foreach (var row in execution.Rows)
            {
                var tagged = new Dictionary<string, object?>(row)
                {
                    [ReportDetail.ParentIndexColumn] = index
                };
                section.Rows.Add(tagged);
            }
        }

        stopwatch.Stop();
        section.ExecutionDurationMs = stopwatch.ElapsedMilliseconds;
        section.TotalRows = section.Rows.Count;

        if (parent.Rows.Count > cap)
        {
            result.Warnings.Add(
                $"'{section.Title}' expanded the first {cap:N0} of {parent.Rows.Count:N0} " +
                $"'{parent.Title}' rows. Raise the report's detail-row limit to include more.");
        }
    }

    /// <summary>
    /// Fills in defaults and enforces the report form's own required fields, so a missing value
    /// is reported against the field the user actually saw rather than against some query
    /// parameter they have never heard of.
    /// </summary>
    private static Dictionary<string, string> ResolveReportParameters(
        Report report,
        IReadOnlyDictionary<string, string> submitted)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in report.Parameters.OrderBy(p => p.SortOrder))
        {
            submitted.TryGetValue(parameter.Name, out var raw);

            if (string.IsNullOrWhiteSpace(raw))
                raw = parameter.DefaultValue;

            if (parameter.IsRequired && string.IsNullOrWhiteSpace(raw))
                throw new DomainException($"Parameter '{parameter.DisplayName}' is required.");

            if (raw is not null)
                values[parameter.Name] = raw;
        }

        return values;
    }

    /// <summary>
    /// Routes report-level values onto one dataset's query parameters. A query parameter with no
    /// map is simply left out, so the query falls back to its own default — a dataset never
    /// fails merely because the report author did not wire every one of its parameters.
    /// </summary>
    private static Dictionary<string, string> BuildQueryParameters(
        ReportDataset dataset,
        IReadOnlyDictionary<string, string> reportValues,
        IReadOnlyDictionary<string, object?>? parentRow)
    {
        var parameters = new Dictionary<string, string>();

        foreach (var map in dataset.ParameterMaps)
        {
            string? value = map.SourceKind switch
            {
                ReportParameterSourceKind.Constant => map.ConstantValue,
                ReportParameterSourceKind.ReportParameter =>
                    LookupReportValue(map, reportValues),
                ReportParameterSourceKind.ParentColumn =>
                    LookupParentValue(map, parentRow),
                _ => null
            };

            if (value is not null)
                parameters[map.TargetParameterName] = value;
        }

        return parameters;
    }

    /// <summary>
    /// Reads one cell of the parent row for a master/detail child. The raw value is passed
    /// through as text; the child query types it against its own parameter metadata, exactly as
    /// it would a value typed into a form.
    /// </summary>
    private static string? LookupParentValue(
        ReportParameterMap map,
        IReadOnlyDictionary<string, object?>? parentRow)
    {
        if (parentRow is null || map.ParentColumn is null)
            return null;

        if (!parentRow.TryGetValue(map.ParentColumn, out var value) || value is null or DBNull)
            return null;

        return value switch
        {
            DateTime date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private static string? LookupReportValue(
        ReportParameterMap map,
        IReadOnlyDictionary<string, string> reportValues)
    {
        if (map.ReportParameter is not null &&
            reportValues.TryGetValue(map.ReportParameter.Name, out var byNavigation))
        {
            return byNavigation;
        }

        return null;
    }

    /// <summary>
    /// The parameter values as the Word template sees them: {{@name}} per parameter and the
    /// {{PARAMS}} summary.
    /// </summary>
    private static List<ExportParameter> BuildExportParameters(
        Report report,
        IReadOnlyDictionary<string, string> values)
    {
        return report.Parameters
            .OrderBy(p => p.SortOrder)
            .Select(p => new ExportParameter(
                p.Name,
                string.IsNullOrWhiteSpace(p.DisplayName) ? p.Name : p.DisplayName,
                values.TryGetValue(p.Name, out var v) ? v : null))
            .ToList();
    }

    /// <summary>
    /// Records the run. The per-dataset <c>QueryExecutionLog</c> rows are still written by
    /// <see cref="ExecuteQueryCommand"/>; this row is what ties them together into the single
    /// action the user actually asked for.
    /// </summary>
    private async Task WriteRunLogAsync(
        Report report,
        Dictionary<string, string> submittedParameters,
        ReportRunResult result,
        CancellationToken cancellationToken)
    {
        var outcomes = result.Sections.Select(s => new
        {
            s.Key,
            s.Title,
            s.TotalRows,
            s.Error
        });

        var run = new ReportRun
        {
            Id = Guid.NewGuid(),
            ReportId = report.Id,
            UserId = _currentUser.UserId,
            ParametersJson = JsonSerializer.Serialize(submittedParameters),
            StartedAt = DateTime.UtcNow,
            DurationMs = result.ExecutionDurationMs,
            IsSuccess = result.IsCompleteSuccess,
            ErrorMessage = result.IsCompleteSuccess
                ? null
                : Truncate(string.Join(" | ", result.Sections.Where(s => !s.IsSuccess)
                    .Select(s => $"{s.Key}: {s.Error}")), 2000),
            DatasetResultsJson = JsonSerializer.Serialize(outcomes),
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.ReportRuns.AddAsync(run, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
