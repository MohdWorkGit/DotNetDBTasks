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

    public DateTime ExecutedAt { get; set; }
    public long ExecutionDurationMs { get; set; }
    public int RowsReturned { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
