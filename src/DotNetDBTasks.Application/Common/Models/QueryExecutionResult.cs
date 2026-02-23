namespace DotNetDBTasks.Application.Common.Models;

/// <summary>
/// Represents the result of a dynamic SQL query execution.
/// For SELECT queries, Columns and Rows are populated.
/// For non-SELECT queries (INSERT, UPDATE, DELETE, etc.), AffectedRows indicates the number of rows affected.
/// </summary>
public class QueryExecutionResult
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public long ExecutionDurationMs { get; set; }

    /// <summary>
    /// Number of rows affected by a non-SELECT query (INSERT, UPDATE, DELETE, MERGE, etc.).
    /// Null for SELECT queries.
    /// </summary>
    public int? AffectedRows { get; set; }
}
