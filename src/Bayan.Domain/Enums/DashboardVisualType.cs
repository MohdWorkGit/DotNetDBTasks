namespace Bayan.Domain.Enums;

/// <summary>
/// How a dashboard tile draws its query's rows. Every shape a report chart has is here too, so a
/// tile is drawn by the same SVG renderer; <see cref="Kpi"/>, <see cref="Table"/>,
/// <see cref="Gauge"/>, <see cref="Funnel"/> and <see cref="Heatmap"/> exist only on dashboards —
/// Word has no native chart for the last three, and a report must draw in Word.
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
    Table = 5,

    Area = 6,

    StackedColumn = 7,

    StackedBar = 8,

    Doughnut = 9,

    /// <summary>Columns plus one or more series drawn as a line, optionally on a second axis.</summary>
    Combo = 10,

    /// <summary>A KPI drawn as a dial from zero to a maximum, with its target marked.</summary>
    Gauge = 11,

    /// <summary>Stages in descending order, each as a share of the first.</summary>
    Funnel = 12,

    /// <summary>
    /// A grid of category × category, each cell shaded by its value. The second category comes
    /// from the tile's split-by column.
    /// </summary>
    Heatmap = 13
}
