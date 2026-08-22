using System.Globalization;
using System.Text.Json;
using Bayan.Application.Common.Models;
using Bayan.Domain.Entities;

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
        if (chart.ChartType == Domain.Enums.ReportChartType.Pie && seriesColumns.Count > 1)
            seriesColumns = seriesColumns.Take(1).ToList();

        // Past a certain number the axis stops being readable, so the tail is dropped rather
        // than drawn illegibly. The reader is told by the section's row count not matching.
        var rows = section.Rows.Take(Math.Max(1, chart.MaxCategories)).ToList();

        var categories = rows
            .Select(row => Text(row.TryGetValue(chart.CategoryColumn, out var v) ? v : null))
            .ToList();

        var series = seriesColumns
            .Select(column => new ReportChartSeries(
                column,
                rows.Select(row => Number(row.TryGetValue(column, out var v) ? v : null)).ToList()))
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

    private static string Text(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>
    /// Coerces a cell to a plottable number. Anything that is not one becomes null — a gap —
    /// rather than zero, because a category with no value and a category worth zero are
    /// different statements and a chart should not conflate them.
    /// </summary>
    private static double? Number(object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return null;
            case double d:
                return d;
            case float f:
                return f;
            case decimal m:
                return (double)m;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            case bool b:
                return b ? 1 : 0;
            case string text:
                return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            default:
                return null;
        }
    }
}
