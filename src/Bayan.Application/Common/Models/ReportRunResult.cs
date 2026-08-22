namespace Bayan.Application.Common.Models;

/// <summary>
/// How a master/detail section marks which parent row each of its rows belongs to.
/// </summary>
public static class ReportDetail
{
    /// <summary>
    /// Column carried on every row of a detail dataset, holding the zero-based index of the
    /// parent row it was fetched for.
    ///
    /// <para>Deliberately an index rather than the parent's key value: the key could be any
    /// type, could collide across drivers that return it differently, and could be absent from
    /// the child's own columns. An index is unambiguous and needs no normalisation.</para>
    ///
    /// <para>It is kept <b>out of</b> the section's <c>Columns</c> list, so nothing that
    /// iterates columns — the grid, Excel, CSV, the Word tables — ever renders it. Only the
    /// region expander, which looks it up by name, knows it is there.</para>
    /// </summary>
    public const string ParentIndexColumn = "__parentIndex";
}

/// <summary>
/// One dataset's outcome inside a report run. Failures are carried per section rather than
/// aborting the whole run: a report is several independent queries, and losing one section is
/// far more useful than losing the document.
/// </summary>
public sealed class ReportSectionResult
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The query behind this section. Carried so the cached-result job filed for it can name a
    /// faithful source rather than an anonymous one.
    /// </summary>
    public Guid? DynamicQueryId { get; set; }

    /// <summary>Column names, in order. Empty when the section failed.</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>
    /// The rows. Held in the run result only long enough to be filed into the result cache and
    /// rendered into an export; the viewer pages them back out of the cache, not from here.
    /// </summary>
    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    public int TotalRows { get; set; }

    public long ExecutionDurationMs { get; set; }

    /// <summary>Null on success; the reason this one section is missing otherwise.</summary>
    public string? Error { get; set; }

    public bool IsSuccess => Error is null;

    /// <summary>Whether the on-screen viewer shows this section as a tab of its own.</summary>
    public bool IsVisibleInViewer { get; set; } = true;
}

/// <summary>
/// The outcome of running a whole report: every dataset, in template order, plus the parameter
/// values the run used so an export can print them.
/// </summary>
public sealed class ReportRunResult
{
    public Guid ReportId { get; set; }
    public string ReportName { get; set; } = string.Empty;

    public List<ReportSectionResult> Sections { get; set; } = new();

    /// <summary>
    /// Charts, already reduced to categories and series. Built once here so the document and
    /// the on-screen view draw the same numbers.
    /// </summary>
    public List<ReportChartData> Charts { get; set; } = new();

    /// <summary>The report-level parameter values, for the {{@name}} and {{PARAMS}} placeholders.</summary>
    public List<ExportParameter> Parameters { get; set; } = new();

    public long ExecutionDurationMs { get; set; }

    /// <summary>
    /// Things the user should know that are not failures — a truncated detail expansion, a row
    /// budget reached. Surfaced rather than silently applied.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>True when every section succeeded.</summary>
    public bool IsCompleteSuccess => Sections.All(s => s.IsSuccess);
}
