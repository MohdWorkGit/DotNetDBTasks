using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;
using Bayan.Application.Common.Reporting;
using Bayan.Application.Common.Security;
using Bayan.Application.Features.Dashboards.Dtos;
using Bayan.Application.Features.QueryExecution.Commands;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Bayan.Application.Features.Dashboards.Queries;

// ---------------------------------------------------------------- column probe

/// <summary>
/// The columns a query returns, so the builder can offer them instead of asking the author to
/// type names that must match exactly. There is no way to learn a query's columns short of running
/// it, so this runs it — through <c>ExecuteQueryCommand</c>, with the caller's own access check,
/// the normal row cap and an execution log row — and returns only the names.
///
/// <para>A failure is returned, not thrown: a probe that cannot run (a required parameter with no
/// value yet) is an ordinary state of a half-built tile, and the form says why and carries on.</para>
/// </summary>
public class ProbeDashboardColumnsQuery : IRequest<DashboardColumnsProbeDto>
{
    public Guid QueryId { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
}

public class ProbeDashboardColumnsQueryHandler : IRequestHandler<ProbeDashboardColumnsQuery, DashboardColumnsProbeDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;

    public ProbeDashboardColumnsQueryHandler(IUnitOfWork unitOfWork, IMediator mediator)
    {
        _unitOfWork = unitOfWork;
        _mediator = mediator;
    }

    public async Task<DashboardColumnsProbeDto> Handle(ProbeDashboardColumnsQuery request, CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(DynamicQuery), request.QueryId);

        // Probing runs the query, so a write query is refused here exactly as it is on a tile.
        if (query.QueryType.IsWrite())
            return new DashboardColumnsProbeDto { Error = "This query changes data, so it cannot feed a tile." };

        // Date parameters may carry a preset — a filter default the tile would send.
        var dateParameters = (await _unitOfWork.QueryParameters.FindAsync(
                p => p.DynamicQueryId == request.QueryId && p.ParameterType == ParameterType.Date, cancellationToken))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var parameters = (request.Parameters ?? new())
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(
                p => p.Key,
                p => dateParameters.Contains(p.Key) ? DatePresets.Resolve(p.Value, DateTime.Today) : p.Value);

        try
        {
            var result = await _mediator.Send(new ExecuteQueryCommand
            {
                QueryId = request.QueryId,
                Parameters = parameters
            }, cancellationToken);

            return new DashboardColumnsProbeDto { Columns = result.Columns };
        }
        catch (Exception ex) when (ex is DomainException or ForbiddenAccessException or NotFoundException)
        {
            return new DashboardColumnsProbeDto { Error = ex.Message };
        }
    }
}

// ---------------------------------------------------------------- scheduled snapshot

/// <summary>
/// A dashboard reduced to what a document needs: one section per tile, in tile order, the charts
/// that go with them, and a note of every tile that could not be produced.
/// </summary>
public sealed class DashboardSnapshot
{
    public string Name { get; init; } = string.Empty;
    public List<ExportResultSet> Sections { get; } = new();
    public List<ReportChartData> Charts { get; } = new();
    public List<ReportTemplateSection> TemplateSections { get; } = new();
    public List<ReportTemplateChart> TemplateCharts { get; } = new();
    public List<ExportParameter> Parameters { get; } = new();
    public List<string> Gaps { get; } = new();
    public int RowCount { get; set; }
}

/// <summary>
/// Runs every tile of a dashboard, fresh, as the current user — for a scheduled task that is the
/// task's creator — and builds the snapshot a scheduled item writes as a PDF, Word or Excel file.
///
/// <para>Each tile goes through the same resolver and producer the live page uses, so its access
/// checks, parameter routing, date presets and KPI rules are identical. The shared tile cache is
/// deliberately bypassed: a snapshot records the data as of its own run.</para>
/// </summary>
public class BuildDashboardSnapshotQuery : IRequest<DashboardSnapshot>
{
    public Guid DashboardId { get; set; }

    /// <summary>Filter values by filter name; missing ones take the filter's default.</summary>
    public Dictionary<string, string> Filters { get; set; } = new();
}

public class BuildDashboardSnapshotQueryHandler : IRequestHandler<BuildDashboardSnapshotQuery, DashboardSnapshot>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IMediator _mediator;
    private readonly ILogger<BuildDashboardSnapshotQueryHandler> _logger;

    public BuildDashboardSnapshotQueryHandler(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IMediator mediator,
        ILogger<BuildDashboardSnapshotQueryHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<DashboardSnapshot> Handle(BuildDashboardSnapshotQuery request, CancellationToken cancellationToken)
    {
        await DashboardAccess.EnsureCanViewAsync(request.DashboardId, _unitOfWork, _currentUser, cancellationToken);

        var dashboard = await _unitOfWork.Dashboards.GetByIdAsync(
            request.DashboardId, cancellationToken, "Tiles", "Filters")
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        if (!dashboard.IsEnabled)
            throw new DomainException("This dashboard is currently disabled.");

        var snapshot = new DashboardSnapshot { Name = dashboard.Name };

        foreach (var filter in dashboard.Filters.OrderBy(f => f.SortOrder))
        {
            request.Filters.TryGetValue(filter.Name, out var raw);
            if (string.IsNullOrWhiteSpace(raw))
                raw = filter.DefaultValue;
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (filter.ParameterType == ParameterType.Date)
                raw = DatePresets.Resolve(raw, DateTime.Today);
            snapshot.Parameters.Add(new ExportParameter(filter.Name, filter.DisplayName,
                filter.AllowMultiple ? ReadableList(raw) : raw));
        }

        var number = 0;
        foreach (var tile in dashboard.Tiles.OrderBy(t => t.SortOrder))
        {
            number++;
            var key = $"tile{number}";

            DashboardTileDataDto data;
            try
            {
                var resolved = await DashboardTileResolver.ResolveAsync(
                    dashboard.Id, tile.Id, request.Filters, _unitOfWork, _currentUser, cancellationToken);
                data = await DashboardTileProducer.ProduceAsync(
                    _mediator, _logger, resolved, TimeSpan.Zero, cancellationToken);
            }
            catch (Exception ex) when (ex is DomainException or ForbiddenAccessException or NotFoundException)
            {
                snapshot.Gaps.Add($"{tile.Title}: {ex.Message}");
                continue;
            }

            if (data.Error is not null)
            {
                snapshot.Gaps.Add($"{tile.Title}: {data.Error}");
                continue;
            }

            var section = ToSection(tile, data, key);
            snapshot.Sections.Add(section);
            snapshot.TemplateSections.Add(new ReportTemplateSection(key, tile.Title));
            snapshot.RowCount += section.Rows.Count;

            if (data.Chart is not null)
            {
                // Marker keys must start with a letter; a tile id may not.
                var chartKey = $"chart{number}";
                snapshot.Charts.Add(data.Chart with { Key = chartKey, Title = tile.Title, DatasetKey = key });
                snapshot.TemplateCharts.Add(new ReportTemplateChart(chartKey, tile.Title, key));
            }
        }

        return snapshot;
    }

    /// <summary>
    /// A multi-select travels as a JSON array; the document header lists it the way a person
    /// would write it.
    /// </summary>
    private static string ReadableList(string raw)
    {
        try
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<List<string>>(raw);
            return items is null ? raw : string.Join(", ", items);
        }
        catch (System.Text.Json.JsonException)
        {
            return raw;
        }
    }

    /// <summary>
    /// A tile as a table: a chart's plotted numbers (not its raw rows — the document shows what the
    /// chart shows), a KPI's figures on one row, or a table tile's own rows.
    /// </summary>
    private static ExportResultSet ToSection(DashboardTile tile, DashboardTileDataDto data, string key)
    {
        if (data.Chart is { } chart)
        {
            var categoryColumn = string.IsNullOrWhiteSpace(tile.CategoryColumn) ? "Category" : tile.CategoryColumn!;
            var columns = new List<string> { categoryColumn };
            columns.AddRange(chart.Series.Select(s => s.Name));

            var rows = chart.Categories
                .Select((category, i) =>
                {
                    var row = new Dictionary<string, object?> { [categoryColumn] = category };
                    foreach (var series in chart.Series)
                        row[series.Name] = series.Values[i];
                    return (IReadOnlyDictionary<string, object?>)row;
                })
                .ToList();

            return new ExportResultSet(columns, rows, key, tile.Title);
        }

        if (data.Kpi is { } kpi)
        {
            var row = new Dictionary<string, object?>
            {
                ["Value"] = kpi.Value,
                ["Compared with"] = kpi.Compare,
                ["Change %"] = kpi.DeltaPercent is { } d ? Math.Round(d, 1) : null,
                ["Target"] = kpi.Target,
                ["Target status"] = kpi.TargetStatus
            };
            return new ExportResultSet(row.Keys.ToList(), new[] { (IReadOnlyDictionary<string, object?>)row }, key, tile.Title);
        }

        if (data.Table is { } table)
        {
            return new ExportResultSet(
                table.Columns,
                table.Rows.Cast<IReadOnlyDictionary<string, object?>>().ToList(),
                key,
                tile.Title);
        }

        // An empty result still gets its section, so the document shows the tile had no data.
        return new ExportResultSet(new List<string> { tile.Title }, Array.Empty<IReadOnlyDictionary<string, object?>>(), key, tile.Title);
    }
}
