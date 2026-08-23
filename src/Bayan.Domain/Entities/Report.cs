using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// An admin-defined document that composes several saved queries into ONE output —
/// the thing a single <see cref="DynamicQuery"/> cannot be: a monthly pack, a branch
/// summary with detail breakdowns, a KPI sheet.
///
/// <para>Layout lives in <see cref="TemplateDocx"/>, a Word document the admin designs
/// and uploads. The report owns the *data* (which datasets, how they relate, which
/// parameters); the template owns the *presentation* and references datasets by their
/// <see cref="ReportDataset.DatasetKey"/> through {{RESULTS:key}} markers. See
/// <c>WordExporter</c> for the full marker grammar.</para>
///
/// <para>Access mirrors queries exactly — by role, by user group, or by name — and is
/// checked <i>in addition to</i> the access check on every underlying query, so a report
/// can never become a way to reach a query the caller could not run directly.</para>
/// </summary>
public class Report : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Nullable because Oracle stores an empty string as NULL.</summary>
    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// Optional group/folder this report belongs to — the <b>same</b> groups queries use, not a
    /// parallel set. A report is just another thing a user runs, so it appears on the same page
    /// and in the same folder as the queries it sits beside, and a grant on the group reaches it
    /// exactly as it reaches those queries.
    /// </summary>
    public Guid? QueryGroupId { get; set; }
    public QueryGroup? QueryGroup { get; set; }

    /// <summary>
    /// The Word (.docx) template that lays the report out. Null means the built-in
    /// starter layout is generated from the report's datasets, so a report renders
    /// something sensible before anyone has designed a template for it.
    /// </summary>
    public byte[]? TemplateDocx { get; set; }

    /// <summary>Original file name of the uploaded template, shown in the admin UI.</summary>
    public string? TemplateFileName { get; set; }

    /// <summary>
    /// Which formats this report may be downloaded as, as a comma-separated list of
    /// <see cref="ExportFileFormat"/> names — e.g. <c>"Word,Pdf"</c>. Null or empty means
    /// it cannot be exported at all and no download button appears.
    ///
    /// <para>Half of a gate: the caller must also hold the matching <c>reports.export*</c>
    /// permission. Same convention and same reasoning as
    /// <see cref="DynamicQuery.AllowedExportFormats"/>.</para>
    /// </summary>
    public string? AllowedExportFormats { get; set; }

    /// <summary>
    /// Ceiling on the whole run — every dataset together, not per dataset. A report fans
    /// out to several queries, so the per-query timeout alone would let a slow report run
    /// for the sum of its parts.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Master/detail guard: the most parent rows a <see cref="ReportDatasetSourceType.Detail"/>
    /// dataset will expand. Detail datasets are N+1 by nature — one child execution per parent
    /// row — so this bounds the damage.
    ///
    /// <para><b>0 means no limit</b>: every parent row is expanded, however many there are. That
    /// is a deliberate choice for an administrator to make, and the run's timeout is then the
    /// only thing bounding it.</para>
    /// </summary>
    public int MaxDetailRows { get; set; } = 100;

    /// <summary>
    /// Whole-report row budget, checked as each dataset completes. Datasets run uncapped so a
    /// report is never silently truncated mid-section, which means without this one report can
    /// materialise several unbounded result sets before anything spills to disk.
    ///
    /// <para><b>0 means no limit</b>, and then nothing stops a report from materialising as many
    /// rows as its queries return.</para>
    /// </summary>
    public int MaxTotalRows { get; set; } = 200_000;

    public ICollection<ReportDataset> Datasets { get; set; } = new List<ReportDataset>();
    public ICollection<ReportParameter> Parameters { get; set; } = new List<ReportParameter>();
    public ICollection<ReportRole> ReportRoles { get; set; } = new List<ReportRole>();
    public ICollection<ReportUserGroup> ReportUserGroups { get; set; } = new List<ReportUserGroup>();
    public ICollection<ReportUser> ReportUsers { get; set; } = new List<ReportUser>();
    public ICollection<ReportChart> Charts { get; set; } = new List<ReportChart>();
    public ICollection<ReportRun> Runs { get; set; } = new List<ReportRun>();
}
