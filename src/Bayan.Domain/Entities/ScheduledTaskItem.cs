using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// One thing a scheduled task runs: a query, or a report.
///
/// <para>Read queries run with their fixed parameter values and are exported to a file in the
/// task's output folder; write queries (INSERT/UPDATE/DELETE) are committed and only their
/// affected-row count is recorded — no file. Read items can be incremental via the Key* fields.</para>
///
/// <para>A report item runs the whole report and writes its document. Incremental checkpoints
/// do not apply to one: a report is a composed document, not a stream of rows to resume.</para>
/// </summary>
public class ScheduledTaskItem : BaseEntity
{
    public Guid ScheduledTaskId { get; set; }
    public ScheduledTask ScheduledTask { get; set; } = null!;

    /// <summary>
    /// The query this item runs. Null when the item runs a <see cref="Report"/> instead —
    /// exactly one of the two is set, which the validator enforces.
    /// </summary>
    public Guid? DynamicQueryId { get; set; }
    public DynamicQuery? DynamicQuery { get; set; }

    /// <summary>
    /// The report this item runs, as an alternative to a single query. A report already
    /// composes several queries into one document, so a report item is never folded into a
    /// task's combined output — it <i>is</i> the combination.
    /// </summary>
    public Guid? ReportId { get; set; }
    public Report? Report { get; set; }

    /// <summary>JSON object of parameter name → value, matching the query's parameter definitions.</summary>
    public string? ParametersJson { get; set; }

    public ExportFileFormat ExportFormat { get; set; }

    /// <summary>
    /// CSV only: the field separator character (e.g. ";", "|", or a tab). Null/empty
    /// means the default comma.
    /// </summary>
    public string? CsvSeparator { get; set; }

    /// <summary>Base file name (without extension). Null/empty falls back to the query name.</summary>
    public string? FileNamePrefix { get; set; }

    /// <summary>
    /// When true (default) a _yyyyMMdd-HHmmss suffix is appended so every run keeps its own
    /// file; when false the same file is overwritten each run (fixed-name feed).
    /// </summary>
    public bool AppendTimestamp { get; set; } = true;

    public int SortOrder { get; set; }

    // --- Incremental checkpoint (read queries only, optional) -------------------------
    // When KeyColumn is set, the saved LastKeyValue (or InitialKey on the first run) is
    // injected as the value of the query parameter named KeyParameter, and after a
    // successful export the KeyColumn value of the LAST returned row becomes the new
    // LastKeyValue. The query should ORDER BY the key ascending.

    /// <summary>Result column whose last-row value is saved as the next run's starting key.</summary>
    public string? KeyColumn { get; set; }

    /// <summary>Name of the query's declared parameter that receives the saved key.</summary>
    public string? KeyParameter { get; set; }

    /// <summary>Key value used before any checkpoint exists.</summary>
    public string? InitialKey { get; set; }

    /// <summary>Where the last successful run stopped; null until the first successful run.</summary>
    public string? LastKeyValue { get; set; }
}
