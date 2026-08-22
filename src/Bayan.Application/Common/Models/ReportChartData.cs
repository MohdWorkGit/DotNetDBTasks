using Bayan.Domain.Enums;

namespace Bayan.Application.Common.Models;

/// <summary>One plotted series: a name and one value per category, aligned by position.</summary>
/// <param name="Name">Legend label — the column the values came from.</param>
/// <param name="Values">
/// One entry per category, in the same order. Null is a gap rather than a zero: a month with no
/// sales and a month with sales of zero are different statements.
/// </param>
public sealed record ReportChartSeries(string Name, IReadOnlyList<double?> Values);

/// <summary>
/// A chart, already reduced to what a renderer needs: categories, series, and nothing about
/// where the numbers came from.
///
/// <para>Aggregation and column selection happen before this, so the two renderers — DrawingML
/// for the document and SVG for the screen — draw from one shape and cannot disagree about what
/// the chart says.</para>
/// </summary>
/// <param name="Key">The token a <c>{{CHART:key}}</c> marker addresses this chart by.</param>
/// <param name="Title">Printed above the plot; empty hides the title.</param>
/// <param name="DatasetKey">
/// The dataset the chart was built from. Carried only so a generated starter template can
/// put the chart's marker under that section; the renderers do not use it.
/// </param>
/// <param name="WidthEmu">
/// Width in English Metric Units, the unit OOXML measures drawings in. 914400 EMU = 1 inch.
/// </param>
public sealed record ReportChartData(
    string Key,
    string Title,
    ReportChartType Type,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ReportChartSeries> Series,
    int WidthEmu = 5486400,     // 6 inches
    int HeightEmu = 3200400,    // 3.5 inches
    string? DatasetKey = null)  // which dataset it drew, for placing its marker
{
    /// <summary>True when there is nothing to draw, so the marker renders no chart at all.</summary>
    public bool IsEmpty => Categories.Count == 0 || Series.Count == 0;
}
