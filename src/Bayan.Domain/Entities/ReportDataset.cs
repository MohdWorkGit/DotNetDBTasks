using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// One named result set inside a <see cref="Report"/>. The Word template addresses it
/// only through <see cref="DatasetKey"/> — {{RESULTS:key}}, {{ROW_COUNT:key}},
/// {{VALUE:key.Column}} — so the three source types below are interchangeable from the
/// template's point of view.
///
/// <para>Every column for all three source types lives on this one table on purpose:
/// Oracle auto-commits DDL, so a migration that adds columns to a table an earlier
/// migration created is the exact failure mode <c>DATABASE_MIGRATION_NOTES.txt</c>
/// Rule 1 exists to prevent. The Join and Detail columns are therefore created up front
/// and simply left null until those source types ship.</para>
/// </summary>
public class ReportDataset : BaseEntity
{
    public Guid ReportId { get; set; }
    public Report Report { get; set; } = null!;

    /// <summary>
    /// The token the template references, e.g. <c>sales</c> for {{RESULTS:sales}}.
    /// Unique within the report and restricted to [A-Za-z0-9_] so it is unambiguous
    /// inside a {{...}} marker and safe as an Excel sheet name.
    /// </summary>
    public string DatasetKey { get; set; } = string.Empty;

    /// <summary>Human-readable heading for the on-screen viewer and the Excel sheet tab.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public ReportDatasetSourceType SourceType { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// When false the dataset still runs and stays available to the template, but the
    /// on-screen viewer does not show it — for intermediate results that only exist to
    /// feed a join or a detail dataset.
    /// </summary>
    public bool IsVisibleInViewer { get; set; } = true;

    // --- Query and Detail sources -----------------------------------------------------

    /// <summary>
    /// The saved query that produces the rows. Set for
    /// <see cref="ReportDatasetSourceType.Query"/> and
    /// <see cref="ReportDatasetSourceType.Detail"/>; null for a join.
    /// </summary>
    public Guid? DynamicQueryId { get; set; }

    /// <summary>
    /// Deleting a query a report depends on is blocked at the database rather than
    /// cascading, for the same reason <see cref="ScheduledTaskItem.DynamicQuery"/> is:
    /// silently hollowing out a report is worse than a loud failure.
    /// </summary>
    public DynamicQuery? DynamicQuery { get; set; }

    // --- Join source ------------------------------------------------------------------

    // The three dataset-to-dataset references below are plain indexed values with no FK
    // constraint, the same shape as QueryParameter.DropdownQueryId. A self-referencing
    // constraint on a table that also cascades from Report makes the delete order matter
    // (ORA-02292), and the application validates these references on save anyway.

    /// <summary>Left input of the join. Another dataset in the same report.</summary>
    public Guid? LeftDatasetId { get; set; }

    /// <summary>Right input of the join.</summary>
    public Guid? RightDatasetId { get; set; }

    public ReportJoinType? JoinType { get; set; }

    /// <summary>Column on the left input matched against <see cref="RightColumn"/>.</summary>
    public string? LeftColumn { get; set; }

    /// <summary>Column on the right input matched against <see cref="LeftColumn"/>.</summary>
    public string? RightColumn { get; set; }

    /// <summary>
    /// Optional JSON array of the output columns to keep, in order, as
    /// <c>[{"source":"left|right","column":"Name","as":"Alias"}]</c>. Null keeps every
    /// column from both sides, with the right side's clashing names suffixed.
    /// </summary>
    public string? ColumnSelectionJson { get; set; }

    // --- Detail source ----------------------------------------------------------------

    /// <summary>
    /// The dataset whose rows drive the repetition. Which parent columns feed which of the
    /// child query's parameters is described by this dataset's
    /// <see cref="ParameterMaps"/> entries with
    /// <see cref="ReportParameterSourceKind.ParentColumn"/> — a child query commonly needs
    /// more than one value from its parent row, so a single column pair here would not do.
    /// </summary>
    public Guid? ParentDatasetId { get; set; }

    public ICollection<ReportParameterMap> ParameterMaps { get; set; } = new List<ReportParameterMap>();
}
