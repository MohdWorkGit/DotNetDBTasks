namespace DotNetDBTasks.Application.Common.Models;

/// <summary>
/// Represents the result of a dynamic SQL query execution.
/// For SELECT queries, Columns and Rows are populated.
/// For non-SELECT queries (INSERT, UPDATE, DELETE, etc.), AffectedRows is populated.
/// </summary>
public class QueryExecutionResult
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public int AffectedRows { get; set; }
    public bool IsLimitReached { get; set; }
    public long ExecutionDurationMs { get; set; }
    public Dictionary<string, object?> Parameters { get; set; } = new();
}
