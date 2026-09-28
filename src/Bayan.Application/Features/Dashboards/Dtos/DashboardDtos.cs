using Bayan.Application.Common.Models;
using Bayan.Domain.Enums;

namespace Bayan.Application.Features.Dashboards.Dtos;

/// <summary>One row of the admin dashboard list, and of the user's dashboard list.</summary>
public class DashboardSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int DefaultRefreshSeconds { get; set; }
    public int SortOrder { get; set; }
    public int TileCount { get; set; }
    public int FilterCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>A dashboard's full definition — layout, filters and tile settings, but no data.</summary>
public class DashboardDto : DashboardSummaryDto
{
    public List<DashboardTileDto> Tiles { get; set; } = new();
    public List<DashboardFilterDto> Filters { get; set; } = new();
}

public class DashboardTileDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid DynamicQueryId { get; set; }
    public string? DynamicQueryName { get; set; }
    public int SortOrder { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DashboardVisualType VisualType { get; set; }
    public string? CategoryColumn { get; set; }
    public List<string> SeriesColumns { get; set; } = new();
    public int MaxCategories { get; set; }
    public string? ValueColumn { get; set; }
    public string? CompareColumn { get; set; }
    public DashboardKpiAggregate KpiAggregate { get; set; }
    public DashboardValueFormat ValueFormat { get; set; }
    public decimal? TargetValue { get; set; }
    public int TargetWarnPercent { get; set; } = 10;
    public List<DashboardConditionalRule> ConditionalRules { get; set; } = new();
    public bool HigherIsBetter { get; set; }

    /// <summary>The tile's own interval, or null when it inherits the dashboard's.</summary>
    public int? RefreshSeconds { get; set; }

    /// <summary>The interval the viewer polls at — the tile's own, or the dashboard default.</summary>
    public int EffectiveRefreshSeconds { get; set; }

    public DashboardDrillAction DrillAction { get; set; }

    /// <summary>The filter a click sets, by name — the builder and the URL both work in names.</summary>
    public string? DrillFilterName { get; set; }

    public Guid? DrillReportId { get; set; }
    public string? DrillReportParameter { get; set; }

    public List<DashboardTileParameterMapDto> ParameterMaps { get; set; } = new();

    /// <summary>
    /// The formats the tile's query may be exported as. The viewer narrows this again by what the
    /// caller's roles permit before offering a download, exactly as the query page does.
    /// </summary>
    public List<string> AllowedExportFormats { get; set; } = new();
}

/// <summary>
/// One colour rule: when <see cref="Column"/> compares to <see cref="Value"/> by
/// <see cref="Operator"/>, the cell (or a KPI's value) takes <see cref="Tone"/>.
/// </summary>
public class DashboardConditionalRule
{
    /// <summary>The column tested. Empty on a KPI tile means the KPI's own value.</summary>
    public string? Column { get; set; }

    /// <summary>One of <see cref="Operators"/>.</summary>
    public string Operator { get; set; } = "gt";

    public string Value { get; set; } = string.Empty;

    /// <summary>One of <see cref="Tones"/>.</summary>
    public string Tone { get; set; } = "bad";

    public static readonly IReadOnlySet<string> Operators =
        new HashSet<string> { "gt", "gte", "lt", "lte", "eq", "neq", "contains" };

    public static readonly IReadOnlySet<string> Tones = new HashSet<string> { "good", "warn", "bad" };
}

public class DashboardTileParameterMapDto
{
    public string TargetParameterName { get; set; } = string.Empty;
    public DashboardTileParameterSource SourceKind { get; set; }
    public string? FilterName { get; set; }
    public string? ConstantValue { get; set; }
}

/// <summary>Mirrors the report parameter shape so the client reuses the same controls.</summary>
public class DashboardFilterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public int SortOrder { get; set; }
    public bool AllowMultiple { get; set; }
    public DropdownSourceType? DropdownSourceType { get; set; }
    public string? DropdownStaticValues { get; set; }
    public Guid? DropdownQueryId { get; set; }
    public string? DropdownQueryValueColumn { get; set; }
    public string? DropdownQueryLabelColumn { get; set; }
}

/// <summary>Who a dashboard is granted to, for the access page.</summary>
public class DashboardAccessDto
{
    public List<Guid> RoleIds { get; set; } = new();
    public List<Guid> UserGroupIds { get; set; } = new();
    public List<Guid> UserIds { get; set; } = new();
}

// ---------------------------------------------------------------- save

/// <summary>
/// What the builder sends when saving. Replaced as a whole graph, as a report is: a tile's maps
/// name filters, and a partial update is how a map ends up pointing at a filter that is gone.
/// </summary>
public class DashboardInput
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int DefaultRefreshSeconds { get; set; } = 60;
    public int SortOrder { get; set; }
    public List<DashboardTileInput> Tiles { get; set; } = new();
    public List<DashboardFilterInput> Filters { get; set; } = new();
}

public class DashboardTileInput
{
    public string Title { get; set; } = string.Empty;
    public Guid DynamicQueryId { get; set; }
    public int SortOrder { get; set; }
    public int Width { get; set; } = 4;
    public int Height { get; set; } = 1;
    public DashboardVisualType VisualType { get; set; }
    public string? CategoryColumn { get; set; }
    public List<string> SeriesColumns { get; set; } = new();
    public int MaxCategories { get; set; } = 25;
    public string? ValueColumn { get; set; }
    public string? CompareColumn { get; set; }
    public DashboardKpiAggregate KpiAggregate { get; set; }
    public DashboardValueFormat ValueFormat { get; set; }
    public decimal? TargetValue { get; set; }
    public int TargetWarnPercent { get; set; } = 10;
    public List<DashboardConditionalRule> ConditionalRules { get; set; } = new();
    public bool HigherIsBetter { get; set; } = true;
    public int? RefreshSeconds { get; set; }
    public DashboardDrillAction DrillAction { get; set; }

    /// <summary>By name, not id: the builder can add a filter and point a tile at it in one save.</summary>
    public string? DrillFilterName { get; set; }

    public Guid? DrillReportId { get; set; }
    public string? DrillReportParameter { get; set; }
    public List<DashboardTileParameterMapDto> ParameterMaps { get; set; } = new();
}

public class DashboardFilterInput
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public int SortOrder { get; set; }
    public bool AllowMultiple { get; set; }
    public DropdownSourceType? DropdownSourceType { get; set; }
    public string? DropdownStaticValues { get; set; }
    public Guid? DropdownQueryId { get; set; }
    public string? DropdownQueryValueColumn { get; set; }
    public string? DropdownQueryLabelColumn { get; set; }
}

// ---------------------------------------------------------------- tile data

/// <summary>
/// One tile's current data. Exactly one of <see cref="Chart"/>, <see cref="Kpi"/> or
/// <see cref="Table"/> is set on success; <see cref="Error"/> is set instead when the tile could
/// not be produced, and the rest of the dashboard carries on regardless.
/// </summary>
public class DashboardTileDataDto
{
    public Guid TileId { get; set; }

    /// <summary>When the underlying query actually ran — older than now when served from the shared cache.</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>When a fresh result will next be available; the viewer schedules its next poll from this.</summary>
    public DateTime NextRefreshAt { get; set; }

    public ReportChartData? Chart { get; set; }
    public DashboardKpiData? Kpi { get; set; }
    public DashboardTableData? Table { get; set; }

    /// <summary>True when the query returned no rows, so the tile says so instead of drawing nothing.</summary>
    public bool IsEmpty { get; set; }

    public string? Error { get; set; }
}

/// <summary>A KPI tile's figures.</summary>
/// <param name="Value">The last row's value, or the tile's aggregate over all rows; null when there is no number.</param>
/// <param name="Compare">In last-row mode the compare column on that row, or else the previous row's value;
/// otherwise the same aggregate over the compare column.</param>
/// <param name="DeltaPercent">Change from <paramref name="Compare"/> in percent; null when there is nothing to compare or it is zero.</param>
/// <param name="Sparkline">The value column down the rows in query order, when there is more than one row.</param>
/// <param name="Target">The tile's target, when it has one.</param>
/// <param name="TargetStatus"><c>met</c>, <c>near</c> or <c>missed</c>; null without a target or a value.</param>
public sealed record DashboardKpiData(
    double? Value,
    double? Compare,
    double? DeltaPercent,
    IReadOnlyList<double?> Sparkline,
    double? Target = null,
    string? TargetStatus = null);

public sealed record DashboardTableData(
    IReadOnlyList<string> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    int TotalRows);

public class DashboardTileDataRequest
{
    /// <summary>The filter bar's values by filter name, in the same wire format as report parameters.</summary>
    public Dictionary<string, string>? Filters { get; set; }
}

/// <summary>What a drill-to-rows click needs: the query to run and the parameters the tile ran it with.</summary>
public class DashboardTileDrillDto
{
    public Guid QueryId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

/// <summary>The columns a query returns, for the builder's pickers — or why they could not be read.</summary>
public class DashboardColumnsProbeDto
{
    public List<string> Columns { get; set; } = new();
    public string? Error { get; set; }
}

public class DashboardColumnsProbeRequest
{
    public Guid QueryId { get; set; }

    /// <summary>Parameter values to run it with, by query parameter name; presets are resolved.</summary>
    public Dictionary<string, string>? Parameters { get; set; }
}
