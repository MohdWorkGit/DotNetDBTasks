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
    /// True when the log stores pre-change row snapshots (UPDATE/DELETE queries). The rows
    /// themselves are large and are fetched on demand, one page at a time, via the
    /// old-values endpoint — they are never included in list responses.
    /// </summary>
    public bool HasOldValues { get; set; }
    /// <summary>
    /// True when the SQL query is a DML UPDATE statement (used to show a before/after view).
    /// </summary>
    public bool IsUpdateQuery { get; set; }
    /// <summary>
    /// True when the SQL query is a DML DELETE statement.
    /// </summary>
    public bool IsDeleteQuery { get; set; }
    public DateTime ExecutedAt { get; set; }
    public long ExecutionDurationMs { get; set; }
    public int RowsReturned { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// One page of pre-change row snapshots from a single execution log's OldValuesJson.
/// </summary>
public class ExecutionLogOldValuesPageDto
{
    public int TotalRows { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    /// <summary>Union of column names across the rows in this page.</summary>
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, string>> Rows { get; set; } = new();
}
