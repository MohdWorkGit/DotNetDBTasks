using System.Text.Json;
using Bayan.Application.Common.Models;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;

namespace Bayan.Application.Common.Reporting;

/// <summary>
/// Turns a dataset's rows into the categories and series a chart is drawn from.
///
/// <para>Runs once per chart and feeds both renderers — the DrawingML part in the document and
/// the SVG on screen — so the two cannot disagree about what the chart says.</para>
/// </summary>
public static class ReportChartBuilder
{
    /// <summary>
    /// Builds the plot data, or null when the chart cannot be drawn — a missing column or an
    /// empty dataset. Returning null rather than an empty chart lets the caller leave the marker
    /// out entirely instead of printing an empty pair of axes.
    /// </summary>
    public static ReportChartData? Build(ReportChart chart, ReportSectionResult section)
    {
        if (section.Rows.Count == 0 || !section.IsSuccess)
            return null;

        if (!section.Columns.Contains(chart.CategoryColumn, StringComparer.OrdinalIgnoreCase))
            return null;

        var seriesColumns = ParseSeriesColumns(chart.SeriesColumnsJson)
            .Where(c => section.Columns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (seriesColumns.Count == 0)
            return null;

        // A pie is parts of one whole, so more than one series has no meaning on it.
        if (IsSingleSeries(chart.ChartType) && seriesColumns.Count > 1)
            seriesColumns = seriesColumns.Take(1).ToList();

        // Past a certain number the axis stops being readable, so the tail is dropped rather
        // than drawn illegibly. The reader is told by the section's row count not matching.
        var rows = section.Rows.Take(Math.Max(1, chart.MaxCategories)).ToList();

        var categories = rows
            .Select(row => CellValues.Text(row.TryGetValue(chart.CategoryColumn, out var v) ? v : null))
            .ToList();

        var series = seriesColumns
            .Select(column => new ReportChartSeries(
                column,
                rows.Select(row => CellValues.Number(row.TryGetValue(column, out var v) ? v : null)).ToList()))
            .ToList();

        return new ReportChartData(
            chart.ChartKey,
            chart.Title ?? string.Empty,
            chart.ChartType,
            categories,
            series,
            // The section's own key: a generated starter template uses it to place the chart's
            // marker under the table it was drawn from.
            DatasetKey: section.Key);
    }

    /// <summary>Parts of one whole: only the first series is drawn.</summary>
    public static bool IsSingleSeries(Domain.Enums.ReportChartType type) =>
        type is Domain.Enums.ReportChartType.Pie or Domain.Enums.ReportChartType.Doughnut;

    /// <summary>
    /// Long-format rows to a chart: one category per distinct <paramref name="categoryColumn"/>
    /// value and one series per distinct <paramref name="splitColumn"/> value, each cell the sum
    /// of <paramref name="valueColumn"/> over the rows that share both. Both orders are the order
    /// values first appear, so a query's ORDER BY still decides the axis.
    /// </summary>
    /// <returns>Null when a column is missing, as <see cref="Build"/> does.</returns>
    public static ReportChartData? Pivot(
        string key,
        ReportChartType type,
        IReadOnlyList<string> columns,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string categoryColumn,
        string splitColumn,
        string valueColumn,
        int maxSeries)
    {
        if (rows.Count == 0 ||
            !columns.Contains(categoryColumn, StringComparer.OrdinalIgnoreCase) ||
            !columns.Contains(splitColumn, StringComparer.OrdinalIgnoreCase) ||
            !columns.Contains(valueColumn, StringComparer.OrdinalIgnoreCase))
            return null;

        var categories = new List<string>();
        var categoryIndex = new Dictionary<string, int>();
        var seriesNames = new List<string>();
        var seriesIndex = new Dictionary<string, int>();
        var cells = new Dictionary<(int, int), double?>();

        foreach (var row in rows)
        {
            var category = CellValues.Text(row.TryGetValue(categoryColumn, out var c) ? c : null);
            var split = CellValues.Text(row.TryGetValue(splitColumn, out var s) ? s : null);
            var value = CellValues.Number(row.TryGetValue(valueColumn, out var v) ? v : null);

            if (!categoryIndex.TryGetValue(category, out var ci))
            {
                ci = categories.Count;
                categoryIndex[category] = ci;
                categories.Add(category);
            }
            if (!seriesIndex.TryGetValue(split, out var si))
            {
                // Past the cap the extra series are dropped rather than drawn as noise; a legend
                // of fifty colours tells nobody anything.
                if (seriesNames.Count >= maxSeries)
                    continue;
                si = seriesNames.Count;
                seriesIndex[split] = si;
                seriesNames.Add(split);
            }

            cells[(ci, si)] = value is null
                ? cells.GetValueOrDefault((ci, si))
                : (cells.GetValueOrDefault((ci, si)) ?? 0) + value;
        }

        var series = seriesNames
            .Select((name, si) => new ReportChartSeries(
                name,
                categories.Select((_, ci) => cells.GetValueOrDefault((ci, si))).ToList()))
            .ToList();

        return new ReportChartData(key, string.Empty, type, categories, series);
    }

    /// <summary>
    /// Orders the categories and caps how many are drawn. <paramref name="sort"/> is one of
    /// <c>none</c>, <c>valueDesc</c>, <c>valueAsc</c>, <c>category</c>; value sorts rank a category
    /// by its total across series. With <paramref name="groupOthers"/>, what falls past
    /// <paramref name="maxCategories"/> is summed into one last category instead of dropped.
    /// </summary>
    public static ReportChartData Shape(ReportChartData chart, string sort, int maxCategories, bool groupOthers)
    {
        var order = Enumerable.Range(0, chart.Categories.Count).ToList();
        double Total(int i) => chart.Series.Sum(s => s.Values[i] ?? 0);

        order = sort switch
        {
            "valueDesc" => order.OrderByDescending(Total).ToList(),
            "valueAsc" => order.OrderBy(Total).ToList(),
            "category" => order.OrderBy(i => chart.Categories[i], CategoryComparer.Instance).ToList(),
            _ => order
        };

        var max = Math.Max(1, maxCategories);
        var kept = order.Take(max).ToList();
        var rest = order.Skip(max).ToList();
        var addOther = groupOthers && rest.Count > 0;

        var categories = kept.Select(i => chart.Categories[i]).ToList();
        if (addOther)
            categories.Add("Other");

        var series = chart.Series
            .Select(s =>
            {
                var values = kept.Select(i => s.Values[i]).ToList();
                if (addOther)
                    values.Add(rest.Sum(i => s.Values[i] ?? 0));
                return new ReportChartSeries(s.Name, values);
            })
            .ToList();

        return chart with { Categories = categories, Series = series, LastIsOther = addOther };
    }

    /// <summary>Numeric labels in numeric order (so "10" follows "9"), anything else as text.</summary>
    private sealed class CategoryComparer : IComparer<string>
    {
        public static readonly CategoryComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (double.TryParse(x, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var a) &&
                double.TryParse(y, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var b))
                return a.CompareTo(b);
            return string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    private static List<string> ParseSeriesColumns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            // A malformed list is treated as no series, which the caller reports as an
            // undrawable chart rather than failing the whole report.
            return new List<string>();
        }
    }
}
