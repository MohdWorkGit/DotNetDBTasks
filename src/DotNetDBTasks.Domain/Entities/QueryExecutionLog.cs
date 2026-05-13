namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Audit log entry for every dynamic query execution.
/// Tracks who executed what, when, and with which parameters.
/// </summary>
public class QueryExecutionLog : BaseEntity
{
    public Guid DynamicQueryId { get; set; }
    public DynamicQuery DynamicQuery { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// JSON-serialized dictionary of parameters used during execution.
    /// </summary>
    public string ParametersJson { get; set; } = string.Empty;

    /// <summary>
    /// For UPDATE/DELETE queries: JSON-serialized array of every affected row (full column
    /// values) as it existed in the database before the change was applied. Each element is
    /// a dictionary keyed by column name. Null for INSERT or when the pre-fetch could not
    /// be performed.
    /// </summary>
    public string? OldValuesJson { get; set; }

    public DateTime ExecutedAt { get; set; }
    public long ExecutionDurationMs { get; set; }
    public int RowsReturned { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
