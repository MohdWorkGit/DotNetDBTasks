namespace Bayan.Domain.Enums;

/// <summary>
/// Where a report dataset's rows come from. Every renderable thing in a report is a
/// dataset with a key, and the Word template references it by that key alone — it does
/// not care which of these produced the rows.
/// </summary>
public enum ReportDatasetSourceType
{
    /// <summary>A saved <see cref="Entities.DynamicQuery"/> run once with the mapped report parameters.</summary>
    Query = 0,

    /// <summary>Two other datasets joined in memory on a key column.</summary>
    Join = 1,

    /// <summary>
    /// A child query re-run once per row of a parent dataset (master/detail). Costs one
    /// execution per parent row, so it is capped by <see cref="Entities.Report.MaxDetailRows"/>.
    /// </summary>
    Detail = 2
}
