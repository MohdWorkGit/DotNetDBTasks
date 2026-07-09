using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// An admin-defined recurring job that runs one or more read queries and writes each
/// result set to a file (Excel/CSV/JSON) in <see cref="OutputFolder"/> on the server.
/// Only admins create/edit/run tasks; other users see the task's status only when
/// listed in <see cref="Viewers"/>.
/// </summary>
public class ScheduledTask : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Nullable because Oracle stores an empty string as NULL.</summary>
    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Absolute server-side folder the export files are written to.</summary>
    public string OutputFolder { get; set; } = string.Empty;

    /// <summary>
    /// Optional second absolute folder that receives a copy of every export file
    /// (e.g. a feed folder consumers empty, plus a permanent archive).
    /// </summary>
    public string? ArchiveFolder { get; set; }

    /// <summary>
    /// When true, all read-query results are appended into ONE output file in item
    /// order (like the standalone QueryRunner) instead of one file per query. The
    /// header row comes from the first query, so the queries should return
    /// compatible columns. Write queries are unaffected — they still just commit.
    /// </summary>
    public bool CombineOutput { get; set; }

    /// <summary>When false, CSV/Excel exports contain data rows only (no header row).</summary>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>Combined mode: base file name without extension (null/empty = task name).</summary>
    public string? CombinedFileName { get; set; }

    /// <summary>Combined mode: format of the single output file.</summary>
    public ExportFileFormat CombinedFormat { get; set; } = ExportFileFormat.Csv;

    /// <summary>Combined mode, CSV only: field separator text (null/empty = comma).</summary>
    public string? CombinedCsvSeparator { get; set; }

    /// <summary>Combined mode: append a run timestamp to the file name (default true).</summary>
    public bool CombinedAppendTimestamp { get; set; } = true;

    /// <summary>
    /// .NET date format for the timestamp suffix of every output file (per-query and
    /// combined). Literal text is allowed, so the leading separator is part of the
    /// format (e.g. "-yyyy-MM-dd"). Null = the default "_yyyyMMdd-HHmmss".
    /// </summary>
    public string? TimestampFormat { get; set; }

    /// <summary>Next scheduled trigger, UTC — the earliest upcoming occurrence across
    /// all <see cref="Triggers"/>. Null while the task is disabled.</summary>
    public DateTime? NextRunAt { get; set; }

    /// <summary>
    /// The admin who created the task. Scheduled runs execute under this user's
    /// identity (access checks and audit logs attribute to them).
    /// </summary>
    public Guid CreatedByUserId { get; set; }

    public ICollection<ScheduledTaskTrigger> Triggers { get; set; } = new List<ScheduledTaskTrigger>();
    public ICollection<ScheduledTaskItem> Items { get; set; } = new List<ScheduledTaskItem>();
    public ICollection<ScheduledTaskRun> Runs { get; set; } = new List<ScheduledTaskRun>();
    public ICollection<ScheduledTaskViewer> Viewers { get; set; } = new List<ScheduledTaskViewer>();
}
