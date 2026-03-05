namespace DotNetDBTasks.Application.Features.QueryExecution.Queries;

public class ExecutionLogDto
{
    public Guid Id { get; set; }
    public Guid DynamicQueryId { get; set; }
    public string QueryName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
    /// <summary>
    /// For UPDATE queries: the column values that existed in the database before the update.
    /// Null for non-UPDATE queries or when old values could not be fetched.
    /// </summary>
    public Dictionary<string, string>? OldValues { get; set; }
    /// <summary>
    /// True when the SQL query is a DML UPDATE statement.
    /// </summary>
    public bool IsUpdateQuery { get; set; }
    public DateTime ExecutedAt { get; set; }
    public long ExecutionDurationMs { get; set; }
    public int RowsReturned { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
