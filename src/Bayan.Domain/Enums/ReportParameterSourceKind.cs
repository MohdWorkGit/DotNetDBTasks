namespace Bayan.Domain.Enums;

/// <summary>
/// Where a dataset's query parameter gets its value from when a report runs.
/// </summary>
public enum ReportParameterSourceKind
{
    /// <summary>A field on the report's run form — the shared-parameter case.</summary>
    ReportParameter = 0,

    /// <summary>A fixed value baked into the report, so the user is never asked for it.</summary>
    Constant = 1,

    /// <summary>
    /// A column of the current row of the parent dataset. This is what makes master/detail
    /// work, and it is why the mapping lives here rather than as a single pair of columns on
    /// the dataset: a child query often needs more than one value from its parent row.
    /// </summary>
    ParentColumn = 2
}
