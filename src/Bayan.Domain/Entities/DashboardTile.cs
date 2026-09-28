using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// One tile on a dashboard: a saved query, and how to draw what it returns.
/// </summary>
public class DashboardTile : BaseEntity
{
    public Guid DashboardId { get; set; }
    public Dashboard Dashboard { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The query feeding the tile. Must be a read query. Deleting a query a tile depends on fails
    /// rather than silently emptying the tile, the same rule report datasets follow.
    /// </summary>
    public Guid DynamicQueryId { get; set; }
    public DynamicQuery DynamicQuery { get; set; } = null!;

    public int SortOrder { get; set; }

    /// <summary>Columns spanned on the dashboard's 12-column grid.</summary>
    public int Width { get; set; } = 4;

    /// <summary>Grid rows spanned, 1 to 3.</summary>
    public int Height { get; set; } = 1;

    public DashboardVisualType VisualType { get; set; }

    // --- Chart tiles: the same fields a report chart has, read by the same builder.

    public string? CategoryColumn { get; set; }

    /// <summary>JSON array of the columns plotted as series.</summary>
    public string SeriesColumnsJson { get; set; } = "[]";

    public int MaxCategories { get; set; } = 25;

    // --- KPI tiles.

    /// <summary>
    /// The headline number. The rows are read as a series in query order, so the headline is
    /// the last row's value and the rows before it draw the sparkline.
    /// </summary>
    public string? ValueColumn { get; set; }

    /// <summary>
    /// Optional value to compare against, read from the same (last) row — typically the previous
    /// period, computed by the query. Without one, the row before the last is the comparison.
    /// </summary>
    public string? CompareColumn { get; set; }

    /// <summary>How the rows become one number. <see cref="DashboardKpiAggregate.Last"/> reads them as a series.</summary>
    public DashboardKpiAggregate KpiAggregate { get; set; }

    public DashboardValueFormat ValueFormat { get; set; }

    /// <summary>
    /// Optional target for a KPI. Met or missed is judged in the direction
    /// <see cref="HigherIsBetter"/> gives, so a ceiling (complaints) works as well as a goal (sales).
    /// </summary>
    public decimal? TargetValue { get; set; }

    /// <summary>How close to the target, in percent of it, still counts as "near" rather than "missed".</summary>
    public int TargetWarnPercent { get; set; } = 10;

    /// <summary>
    /// JSON list of colour rules, e.g. <c>[{"column":"Total","operator":"lt","value":"100","tone":"bad"}]</c>.
    /// Applied in the browser to table cells and to a KPI's value; the first matching rule wins.
    /// </summary>
    public string? ConditionalRulesJson { get; set; }

    /// <summary>Colours a rise green (true) or red (false). Complaints falling is good news.</summary>
    public bool HigherIsBetter { get; set; } = true;

    /// <summary>Overrides <see cref="Dashboard.DefaultRefreshSeconds"/> for this tile. Null inherits it.</summary>
    public int? RefreshSeconds { get; set; }

    // --- Drill-through.

    public DashboardDrillAction DrillAction { get; set; }

    /// <summary>
    /// The filter a click sets, for <see cref="DashboardDrillAction.FilterDashboard"/>. No FK: it
    /// lives on the same dashboard and is validated on save.
    /// </summary>
    public Guid? DrillFilterId { get; set; }

    /// <summary>
    /// The report a click opens, for <see cref="DashboardDrillAction.OpenReport"/>. No FK, resolved
    /// at run time, so deleting the report leaves the tile working with drill-through switched off.
    /// </summary>
    public Guid? DrillReportId { get; set; }

    /// <summary>The report parameter that receives the clicked category.</summary>
    public string? DrillReportParameter { get; set; }

    public ICollection<DashboardTileParameterMap> ParameterMaps { get; set; } = new List<DashboardTileParameterMap>();
}
