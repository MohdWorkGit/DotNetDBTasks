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
    Pie = 3
}
