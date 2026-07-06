namespace DotNetDBTasks.Application.Common.Models;

/// <summary>
/// Outcome of exporting one query item during a scheduled task run. Serialized as a
/// JSON array into <c>ScheduledTaskRun.ItemResultsJson</c> and returned to the client
/// inside the run DTO.
/// </summary>
public class ScheduledTaskItemResult
{
    public string QueryName { get; set; } = string.Empty;

    /// <summary>Null for write queries — they commit changes and produce no file.</summary>
    public string? FileName { get; set; }

    public bool Success { get; set; }

    /// <summary>Rows exported (read query) or rows affected (write query).</summary>
    public int RowCount { get; set; }

    /// <summary>True when the item ran a data-modifying query (INSERT/UPDATE/DELETE).</summary>
    public bool IsWrite { get; set; }

    /// <summary>New checkpoint saved by this run, for incremental read items.</summary>
    public string? LastKey { get; set; }

    public string? Error { get; set; }
    public long DurationMs { get; set; }
}
