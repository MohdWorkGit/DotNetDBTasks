namespace Bayan.Domain.Enums;

/// <summary>
/// How a dashboard tile draws its query's rows. The chart shapes are the same four a report
/// chart has, so a tile is drawn by the same SVG renderer; <see cref="Kpi"/> and
/// <see cref="Table"/> exist only on dashboards.
/// </summary>
public enum DashboardVisualType
{
    /// <summary>One headline number, optionally against a comparison value and with a sparkline.</summary>
    Kpi = 0,

    Column = 1,

    Bar = 2,

    Line = 3,

    Pie = 4,

    /// <summary>The first rows of the result as a plain grid.</summary>
    Table = 5
}
