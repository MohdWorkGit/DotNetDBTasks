using System.Text.RegularExpressions;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Features.Reports.Dtos;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using Bayan.Domain.Services;
using MediatR;

namespace Bayan.Application.Features.Reports.Commands;

/// <summary>
/// Creates a report, or replaces an existing one's whole definition.
///
/// <para>The datasets, parameters and parameter maps are saved as one graph rather than diffed
/// field by field. They are a single design that only makes sense together — a map points at
/// both a dataset and a parameter — and a partial update is exactly how a report ends up with a
/// map referring to something that no longer exists.</para>
/// </summary>
public class SaveReportCommand : IRequest<ReportDto>
{
    /// <summary>Null creates; set updates that report.</summary>
    public Guid? Id { get; set; }

    public ReportInput Input { get; set; } = new();
}

public partial class SaveReportCommandHandler : IRequestHandler<SaveReportCommand, ReportDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public SaveReportCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<ReportDto> Handle(SaveReportCommand request, CancellationToken cancellationToken)
    {
        var input = request.Input;
        Validate(input);
        await EnsureQueriesExistAsync(input, cancellationToken);

        Report report;
        if (request.Id is null)
        {
            report = new Report
            {
                Id = Guid.NewGuid(),
                CreatedByUserId = _currentUser.UserId,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Reports.AddAsync(report, cancellationToken);
        }
        else
        {
            report = await _unitOfWork.Reports.GetByIdAsync(
                request.Id.Value, cancellationToken, "Datasets", "Datasets.ParameterMaps", "Parameters", "Charts")
                ?? throw new NotFoundException(nameof(Report), request.Id.Value);

            // Replace the graph. Maps cascade from their dataset, so removing the datasets
            // takes them with it.
            foreach (var dataset in report.Datasets.ToList())
                _unitOfWork.ReportDatasets.Delete(dataset);
            foreach (var parameter in report.Parameters.ToList())
                _unitOfWork.ReportParameters.Delete(parameter);
            foreach (var chart in report.Charts.ToList())
                _unitOfWork.ReportCharts.Delete(chart);

            report.UpdatedAt = DateTime.UtcNow;
        }

        report.Name = input.Name.Trim();
        // Nullable rather than empty: Oracle stores '' as NULL, so a nullable column is what is meant.
        report.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        report.IsEnabled = input.IsEnabled;
        report.QueryGroupId = input.QueryGroupId;
        report.TimeoutSeconds = input.TimeoutSeconds;
        report.MaxDetailRows = input.MaxDetailRows;
        report.MaxTotalRows = input.MaxTotalRows;
        // Re-parsed rather than stored verbatim, so an unknown or duplicated name cannot reach
        // the column and the stored order is canonical.
        report.AllowedExportFormats = ExportPermissions.Serialize(
            ExportPermissions.Parse(string.Join(',', input.AllowedExportFormats)));

        // Ids are assigned up front so parameter maps can reference both sides in one save.
        var parameterIdsByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameterInput in input.Parameters)
        {
            var parameter = BuildParameter(report.Id, parameterInput);
            parameterIdsByName[parameter.Name] = parameter.Id;
            await _unitOfWork.ReportParameters.AddAsync(parameter, cancellationToken);
        }

        // Dataset ids are assigned before anything is added, so a Detail dataset can point at a
        // parent that is being created in the very same save.
        var datasets = input.Datasets
            .ToDictionary(d => d.DatasetKey, d => BuildDataset(report.Id, d), StringComparer.OrdinalIgnoreCase);

        foreach (var datasetInput in input.Datasets)
        {
            var dataset = datasets[datasetInput.DatasetKey];

            if (datasetInput.ParentDatasetKey is not null &&
                datasets.TryGetValue(datasetInput.ParentDatasetKey, out var parent))
            {
                dataset.ParentDatasetId = parent.Id;
            }

            if (datasetInput.LeftDatasetKey is not null &&
                datasets.TryGetValue(datasetInput.LeftDatasetKey, out var leftSide))
            {
                dataset.LeftDatasetId = leftSide.Id;
            }

            if (datasetInput.RightDatasetKey is not null &&
                datasets.TryGetValue(datasetInput.RightDatasetKey, out var rightSide))
            {
                dataset.RightDatasetId = rightSide.Id;
            }

            await _unitOfWork.ReportDatasets.AddAsync(dataset, cancellationToken);

            foreach (var mapInput in datasetInput.ParameterMaps)
            {
                var map = BuildMap(dataset.Id, mapInput, parameterIdsByName);
                if (map is not null)
                    await _unitOfWork.ReportParameterMaps.AddAsync(map, cancellationToken);
            }
        }

        foreach (var chartInput in input.Charts)
        {
            if (!datasets.TryGetValue(chartInput.DatasetKey ?? string.Empty, out var chartDataset))
                continue;   // Validate() has already rejected this

            await _unitOfWork.ReportCharts.AddAsync(new ReportChart
            {
                Id = Guid.NewGuid(),
                ReportId = report.Id,
                ChartKey = chartInput.ChartKey.Trim(),
                Title = string.IsNullOrWhiteSpace(chartInput.Title) ? null : chartInput.Title.Trim(),
                ChartType = chartInput.ChartType,
                DatasetId = chartDataset.Id,
                CategoryColumn = chartInput.CategoryColumn.Trim(),
                SeriesColumnsJson = System.Text.Json.JsonSerializer.Serialize(chartInput.SeriesColumns),
                MaxCategories = chartInput.MaxCategories <= 0 ? 25 : chartInput.MaxCategories,
                SortOrder = chartInput.SortOrder,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await ReportMapper.LoadDtoAsync(_unitOfWork, report.Id, cancellationToken);
    }

    private static void Validate(ReportInput input)
    {
        // Only the sign is checked. How large these may be is the administrator's call — the
        // form states what each one costs — and 0 is the explicit way to say "no limit".
        if (input.MaxDetailRows < 0)
            throw new DomainException("Maximum detail rows cannot be negative. Use 0 for no limit.");

        if (input.MaxTotalRows < 0)
            throw new DomainException("The row limit cannot be negative. Use 0 for no limit.");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataset in input.Datasets)
        {
            // Held in a local so the uniqueness check below reads the same value the pattern
            // check accepted, and so it is plainly non-null: the pattern requires a character.
            var datasetKey = dataset.DatasetKey ?? string.Empty;

            if (!DatasetKeyRegex().IsMatch(datasetKey))
            {
                throw new DomainException(
                    $"Dataset key '{dataset.DatasetKey}' is not valid. Use letters, numbers and " +
                    "underscores only — the key appears inside a {{RESULTS:key}} marker and as an " +
                    "Excel sheet name.");
            }

            if (!keys.Add(datasetKey))
                throw new DomainException($"Dataset key '{dataset.DatasetKey}' is used more than once.");

            if (dataset.SourceType == ReportDatasetSourceType.Query && dataset.DynamicQueryId is null)
                throw new DomainException($"Dataset '{dataset.DatasetKey}' has no query assigned.");


            if (dataset.SourceType == ReportDatasetSourceType.Join)
            {
                if (string.IsNullOrWhiteSpace(dataset.LeftDatasetKey) ||
                    string.IsNullOrWhiteSpace(dataset.RightDatasetKey))
                {
                    throw new DomainException($"Join '{dataset.DatasetKey}' needs two datasets to combine.");
                }

                if (string.Equals(dataset.LeftDatasetKey, dataset.RightDatasetKey, StringComparison.OrdinalIgnoreCase))
                    throw new DomainException($"Join '{dataset.DatasetKey}' cannot join a dataset to itself.");

                if (string.IsNullOrWhiteSpace(dataset.LeftColumn) || string.IsNullOrWhiteSpace(dataset.RightColumn))
                    throw new DomainException($"Join '{dataset.DatasetKey}' needs a column from each side to match on.");

                foreach (var side in new[] { dataset.LeftDatasetKey, dataset.RightDatasetKey })
                {
                    if (string.Equals(side, dataset.DatasetKey, StringComparison.OrdinalIgnoreCase))
                        throw new DomainException($"Join '{dataset.DatasetKey}' cannot read from itself.");

                    if (!input.Datasets.Any(d => string.Equals(d.DatasetKey, side, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new DomainException(
                            $"Join '{dataset.DatasetKey}' reads from '{side}', which this report does not have.");
                    }
                }
            }

            if (dataset.SourceType == ReportDatasetSourceType.Detail)
            {
                if (dataset.DynamicQueryId is null)
                    throw new DomainException($"Dataset '{dataset.DatasetKey}' has no query assigned.");

                if (string.IsNullOrWhiteSpace(dataset.ParentDatasetKey))
                {
                    throw new DomainException(
                        $"Dataset '{dataset.DatasetKey}' repeats per row of another dataset, so it needs a parent.");
                }

                if (string.Equals(dataset.ParentDatasetKey, dataset.DatasetKey, StringComparison.OrdinalIgnoreCase))
                    throw new DomainException($"Dataset '{dataset.DatasetKey}' cannot be its own parent.");
            }
        }

        // Parents must exist, and the chain must terminate. One level is the supported depth:
        // two would be parents x children x grandchildren executions, which is not a report but
        // an outage.
        foreach (var dataset in input.Datasets.Where(d => d.ParentDatasetKey is not null))
        {
            var parent = input.Datasets.FirstOrDefault(
                d => string.Equals(d.DatasetKey, dataset.ParentDatasetKey, StringComparison.OrdinalIgnoreCase));

            if (parent is null)
            {
                throw new DomainException(
                    $"Dataset '{dataset.DatasetKey}' names parent '{dataset.ParentDatasetKey}', " +
                    "which this report does not have.");
            }

            if (parent.ParentDatasetKey is not null)
            {
                throw new DomainException(
                    $"Dataset '{dataset.DatasetKey}' repeats under '{parent.DatasetKey}', which itself " +
                    "repeats under another dataset. Only one level of nesting is supported.");
            }
        }

        // Charts share the dataset key namespace, so {{RESULTS:x}} and {{CHART:x}} can never
        // refer to two different things.
        foreach (var chart in input.Charts)
        {
            if (!DatasetKeyRegex().IsMatch(chart.ChartKey ?? string.Empty))
                throw new DomainException($"Chart key '{chart.ChartKey}' is not valid. Use letters, numbers and underscores only.");

            if (!keys.Add(chart.ChartKey!))
                throw new DomainException($"'{chart.ChartKey}' is used by both a dataset and a chart.");

            if (!input.Datasets.Any(d => string.Equals(d.DatasetKey, chart.DatasetKey, StringComparison.OrdinalIgnoreCase)))
                throw new DomainException($"Chart '{chart.ChartKey}' reads from '{chart.DatasetKey}', which this report does not have.");

            if (string.IsNullOrWhiteSpace(chart.CategoryColumn))
                throw new DomainException($"Chart '{chart.ChartKey}' needs a category column.");

            if (chart.SeriesColumns.Count == 0)
                throw new DomainException($"Chart '{chart.ChartKey}' needs at least one series column.");
        }

        var parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in input.Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
                throw new DomainException("A report parameter has no name.");
            if (!parameterNames.Add(parameter.Name))
                throw new DomainException($"Parameter '{parameter.Name}' is declared more than once.");
        }

        foreach (var dataset in input.Datasets)
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in dataset.ParameterMaps)
            {
                if (string.IsNullOrWhiteSpace(map.TargetParameterName))
                    throw new DomainException($"Dataset '{dataset.DatasetKey}' has a mapping with no target parameter.");

                if (!targets.Add(map.TargetParameterName))
                {
                    throw new DomainException(
                        $"Dataset '{dataset.DatasetKey}' maps '{map.TargetParameterName}' more than once.");
                }

                if (map.SourceKind == ReportParameterSourceKind.ReportParameter &&
                    (map.ReportParameterName is null || !parameterNames.Contains(map.ReportParameterName)))
                {
                    throw new DomainException(
                        $"Dataset '{dataset.DatasetKey}' maps '{map.TargetParameterName}' to report " +
                        $"parameter '{map.ReportParameterName}', which this report does not declare.");
                }
            }
        }
    }

    /// <summary>
    /// Confirms every referenced query exists before anything is written, so a bad id fails as
    /// one clear message rather than as a foreign-key violation.
    /// </summary>
    private async Task EnsureQueriesExistAsync(ReportInput input, CancellationToken cancellationToken)
    {
        foreach (var dataset in input.Datasets
            .Where(d => d.SourceType != ReportDatasetSourceType.Join && d.DynamicQueryId is not null))
        {
            var exists = await _unitOfWork.DynamicQueries.ExistsAsync(
                q => q.Id == dataset.DynamicQueryId!.Value, cancellationToken);
            if (!exists)
                throw new NotFoundException(nameof(DynamicQuery), dataset.DynamicQueryId!.Value);
        }
    }

    private static ReportParameter BuildParameter(Guid reportId, ReportParameterInput input) => new()
    {
        Id = Guid.NewGuid(),
        ReportId = reportId,
        Name = input.Name.Trim(),
        DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? input.Name.Trim() : input.DisplayName.Trim(),
        ParameterType = input.ParameterType,
        IsRequired = input.IsRequired,
        DefaultValue = string.IsNullOrWhiteSpace(input.DefaultValue) ? null : input.DefaultValue,
        SortOrder = input.SortOrder,
        AllowMultiple = input.AllowMultiple,
        DropdownSourceType = input.DropdownSourceType,
        DropdownStaticValues = string.IsNullOrWhiteSpace(input.DropdownStaticValues) ? null : input.DropdownStaticValues,
        DropdownQueryId = input.DropdownQueryId,
        DropdownQueryValueColumn = string.IsNullOrWhiteSpace(input.DropdownQueryValueColumn) ? null : input.DropdownQueryValueColumn,
        DropdownQueryLabelColumn = string.IsNullOrWhiteSpace(input.DropdownQueryLabelColumn) ? null : input.DropdownQueryLabelColumn,
        CreatedAt = DateTime.UtcNow
    };

    private static ReportDataset BuildDataset(Guid reportId, ReportDatasetInput input) => new()
    {
        Id = Guid.NewGuid(),
        ReportId = reportId,
        DatasetKey = input.DatasetKey.Trim(),
        DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? input.DatasetKey.Trim() : input.DisplayName.Trim(),
        SourceType = input.SourceType,
        SortOrder = input.SortOrder,
        IsVisibleInViewer = input.IsVisibleInViewer,
        // A join has no query of its own; its rows come from the two datasets it combines.
        DynamicQueryId = input.SourceType == ReportDatasetSourceType.Join ? null : input.DynamicQueryId,
        JoinType = input.SourceType == ReportDatasetSourceType.Join
            ? input.JoinType ?? ReportJoinType.Inner
            : null,
        LeftColumn = string.IsNullOrWhiteSpace(input.LeftColumn) ? null : input.LeftColumn.Trim(),
        RightColumn = string.IsNullOrWhiteSpace(input.RightColumn) ? null : input.RightColumn.Trim(),
        CreatedAt = DateTime.UtcNow
    };

    private static ReportParameterMap? BuildMap(
        Guid datasetId,
        ReportParameterMapInput input,
        IReadOnlyDictionary<string, Guid> parameterIdsByName)
    {
        Guid? reportParameterId = null;
        if (input.SourceKind == ReportParameterSourceKind.ReportParameter)
        {
            if (input.ReportParameterName is null ||
                !parameterIdsByName.TryGetValue(input.ReportParameterName, out var id))
            {
                return null; // Validate() has already rejected this; belt and braces
            }
            reportParameterId = id;
        }

        return new ReportParameterMap
        {
            Id = Guid.NewGuid(),
            ReportDatasetId = datasetId,
            TargetParameterName = input.TargetParameterName.Trim(),
            SourceKind = input.SourceKind,
            ReportParameterId = reportParameterId,
            ConstantValue = string.IsNullOrWhiteSpace(input.ConstantValue) ? null : input.ConstantValue,
            ParentColumn = string.IsNullOrWhiteSpace(input.ParentColumn) ? null : input.ParentColumn,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Safe inside a {{...}} marker and as an Excel sheet name, which is why punctuation and
    /// spaces are excluded rather than merely discouraged.
    /// </summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,99}$")]
    private static partial Regex DatasetKeyRegex();
}
