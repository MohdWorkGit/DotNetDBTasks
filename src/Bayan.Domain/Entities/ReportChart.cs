using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// A chart drawn from one of the report's datasets, placed by a <c>{{CHART:key}}</c> marker.
///
/// <para>A chart is defined against a dataset rather than carrying its own query: the numbers
/// on a chart and the numbers in the table beside it should be the same numbers, fetched once.</para>
/// </summary>
public class ReportChart : BaseEntity
{
    public Guid ReportId { get; set; }
    public Report Report { get; set; } = null!;

    /// <summary>
    /// The token the template addresses this chart by. Shares a namespace with
    /// <see cref="ReportDataset.DatasetKey"/> so <c>{{RESULTS:x}}</c> and <c>{{CHART:x}}</c>
    /// can never mean two different things.
    /// </summary>
    public string ChartKey { get; set; } = string.Empty;

    /// <summary>Printed above the plot. Empty draws no title.</summary>
    public string? Title { get; set; }

    public ReportChartType ChartType { get; set; }

    /// <summary>The dataset supplying the rows. No navigation: it is resolved by id at run time.</summary>
    public Guid DatasetId { get; set; }

    /// <summary>Column whose values label the category axis.</summary>
    public string CategoryColumn { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of the columns plotted as series, e.g. <c>["الإجمالي","الكمية"]</c>. Stored as
    /// JSON rather than a child table because it is an ordered list the UI edits as a whole.
    /// </summary>
    public string SeriesColumnsJson { get; set; } = "[]";

    /// <summary>
    /// The most categories to plot. Beyond this the chart stops being readable, so the rest are
    /// dropped rather than drawn into an unreadable axis.
    /// </summary>
    public int MaxCategories { get; set; } = 25;

    public int SortOrder { get; set; }
}
