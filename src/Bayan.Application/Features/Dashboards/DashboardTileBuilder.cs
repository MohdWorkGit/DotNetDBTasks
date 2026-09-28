using System.Text.Json;
using Bayan.Application.Common.Models;
using Bayan.Application.Common.Reporting;
using Bayan.Application.Features.Dashboards.Dtos;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;

namespace Bayan.Application.Features.Dashboards;

/// <summary>
/// Turns a tile's query result into what the tile draws. Charts go through
/// <see cref="ReportChartBuilder"/> unchanged, so a dashboard chart and a report chart built from
/// the same rows are the same chart.
/// </summary>
public static class DashboardTileBuilder
{
    /// <summary>How many rows a table tile carries. The tile is a glance, not a grid.</summary>
    public const int TableRowLimit = 100;

    /// <summary>The most points a KPI sparkline draws; the newest are kept.</summary>
    public const int SparklineLimit = 60;

    public static void Fill(DashboardTileDataDto data, DashboardTile tile, QueryExecutionResult result)
    {
        // No rows is "no data" — except for a row count, where it is the answer: zero.
        var countsRows = tile.VisualType == DashboardVisualType.Kpi && tile.KpiAggregate == DashboardKpiAggregate.Count;
        if (result.Rows.Count == 0 && !countsRows)
        {
            data.IsEmpty = true;
            return;
        }

        switch (tile.VisualType)
        {
            case DashboardVisualType.Kpi:
                data.Kpi = BuildKpi(tile, result);
                if (data.Kpi is null)
                    data.Error = $"The query returned no column named '{tile.ValueColumn}'.";
                break;

            case DashboardVisualType.Table:
                data.Table = new DashboardTableData(
                    result.Columns,
                    result.Rows.Take(TableRowLimit).ToList(),
                    result.TotalRows);
                break;

            default:
                data.Chart = BuildChart(tile, result);
                if (data.Chart is null)
                    data.Error = "The columns this chart plots are not in the query's result.";
                break;
        }
    }

    private static ReportChartData? BuildChart(DashboardTile tile, QueryExecutionResult result)
    {
        // A transient chart definition: the builder only reads these fields.
        var chart = new ReportChart
        {
            ChartKey = tile.Id.ToString("N"),
            Title = string.Empty,   // the tile header already shows the title
            ChartType = tile.VisualType switch
            {
                DashboardVisualType.Bar => ReportChartType.Bar,
                DashboardVisualType.Line => ReportChartType.Line,
                DashboardVisualType.Pie => ReportChartType.Pie,
                _ => ReportChartType.Column
            },
            CategoryColumn = ResolveColumn(result, tile.CategoryColumn) ?? tile.CategoryColumn ?? string.Empty,
            SeriesColumnsJson = JsonSerializer.Serialize(
                DashboardMapper.ParseSeriesColumns(tile.SeriesColumnsJson)
                    .Select(c => ResolveColumn(result, c) ?? c)),
            MaxCategories = tile.MaxCategories
        };

        var section = new ReportSectionResult
        {
            Key = chart.ChartKey,
            Title = tile.Title,
            Columns = result.Columns,
            Rows = result.Rows,
            TotalRows = result.TotalRows
        };

        return ReportChartBuilder.Build(chart, section);
    }

    /// <summary>
    /// In <see cref="DashboardKpiAggregate.Last"/> mode the rows are a series in the order the
    /// query returns them — oldest first — so the headline is the <b>last</b> row, and the
    /// comparison is the compare column on that row or else the row before it. That makes
    /// <c>SELECT month, total ... ORDER BY month</c> a sparkline with "this month against last"
    /// without any further setup.
    ///
    /// <para>Every other mode summarises all rows — the sum of a breakdown, its average, its size —
    /// and the comparison is the same summary of the compare column, when there is one.</para>
    /// </summary>
    private static DashboardKpiData? BuildKpi(DashboardTile tile, QueryExecutionResult result)
    {
        var rows = result.Rows;
        var valueColumn = ResolveColumn(result, tile.ValueColumn);
        var compareColumn = ResolveColumn(result, tile.CompareColumn);

        if (valueColumn is null && tile.KpiAggregate != DashboardKpiAggregate.Count)
            return null;

        double? value;
        double? compare;

        if (tile.KpiAggregate == DashboardKpiAggregate.Last)
        {
            var last = rows[^1];
            value = CellValues.Number(last.GetValueOrDefault(valueColumn!));
            compare = compareColumn is not null
                ? CellValues.Number(last.GetValueOrDefault(compareColumn))
                : rows.Count > 1
                    ? CellValues.Number(rows[^2].GetValueOrDefault(valueColumn!))
                    : null;
        }
        else
        {
            value = Aggregate(tile.KpiAggregate, rows, valueColumn);
            compare = compareColumn is not null ? Aggregate(tile.KpiAggregate, rows, compareColumn) : null;
        }

        double? delta = value is { } v && compare is { } c && c != 0
            ? (v - c) / Math.Abs(c) * 100
            : null;

        var sparkline = rows.Count > 1 && valueColumn is not null
            ? rows.Skip(Math.Max(0, rows.Count - SparklineLimit))
                .Select(r => CellValues.Number(r.GetValueOrDefault(valueColumn)))
                .ToList()
            : new List<double?>();

        double? target = tile.TargetValue is { } t ? (double)t : null;
        return new DashboardKpiData(value, compare, delta, sparkline, target,
            TargetStatus(value, target, tile.HigherIsBetter, tile.TargetWarnPercent));
    }

    /// <summary>
    /// Summarises one column over every row. Cells that are not numbers are skipped, not counted as
    /// zero, for the reason <see cref="CellValues.Number"/> gives; <c>Count</c> counts rows.
    /// </summary>
    private static double? Aggregate(
        DashboardKpiAggregate mode,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string? column)
    {
        if (mode == DashboardKpiAggregate.Count)
            return rows.Count;

        var numbers = rows
            .Select(r => CellValues.Number(r.GetValueOrDefault(column!)))
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToList();

        if (numbers.Count == 0)
            return null;

        return mode switch
        {
            DashboardKpiAggregate.Sum => numbers.Sum(),
            DashboardKpiAggregate.Average => numbers.Average(),
            DashboardKpiAggregate.Min => numbers.Min(),
            DashboardKpiAggregate.Max => numbers.Max(),
            _ => null
        };
    }

    /// <summary>
    /// Met, near or missed, judged in the direction the tile says is good: for a ceiling
    /// ("higher is better" off) staying at or under the target is met. Near means within
    /// <paramref name="warnPercent"/> percent of the target on the wrong side of it.
    /// </summary>
    public static string? TargetStatus(double? value, double? target, bool higherIsBetter, int warnPercent)
    {
        if (value is not { } v || target is not { } t)
            return null;

        var met = higherIsBetter ? v >= t : v <= t;
        if (met)
            return "met";

        var margin = Math.Abs(t) * warnPercent / 100.0;
        return Math.Abs(v - t) <= margin ? "near" : "missed";
    }

    /// <summary>
    /// The result's own spelling of a column. Oracle upper-cases unquoted aliases, so a tile set up
    /// as "Total" should still find TOTAL rather than silently drawing nothing.
    /// </summary>
    private static string? ResolveColumn(QueryExecutionResult result, string? column)
    {
        if (string.IsNullOrWhiteSpace(column))
            return null;

        return result.Columns.FirstOrDefault(c => string.Equals(c, column, StringComparison.Ordinal))
            ?? result.Columns.FirstOrDefault(c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase));
    }
}
