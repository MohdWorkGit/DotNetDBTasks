namespace Bayan.Domain.Enums;

/// <summary>
/// The chart shapes a report can draw. Deliberately few: each has to be emitted as DrawingML
/// for the document and as hand-rolled SVG for the screen, with no chart library on either
/// side, so every addition is real work in two places.
/// </summary>
public enum ReportChartType
{
    /// <summary>Vertical bars — the default for comparing a value across categories.</summary>
    Column = 0,

    /// <summary>Horizontal bars. Better when category labels are long, which Arabic ones often are.</summary>
    Bar = 1,

    /// <summary>A line per series, for a value over an ordered category axis such as a date.</summary>
    Line = 2,

    /// <summary>Parts of a whole. Uses the first series only; more than one has no meaning here.</summary>
    Pie = 3,

    /// <summary>A line with the area beneath it filled — volume over an ordered axis.</summary>
    Area = 4,

    /// <summary>Vertical bars with each category's series stacked into one bar.</summary>
    StackedColumn = 5,

    /// <summary>Horizontal bars, stacked.</summary>
    StackedBar = 6,

    /// <summary>A pie with a hole. Same single-series rule as <see cref="Pie"/>.</summary>
    Doughnut = 7,

    /// <summary>
    /// Columns for every series but the last, which is drawn as a line on its own (secondary)
    /// value axis — revenue against margin %, where the two scales differ.
    /// </summary>
    Combo = 8
}
