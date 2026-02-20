namespace DotNetDBTasks.Application.Common.Models;

/// <summary>
/// Represents the result of a dynamic SQL query execution.
/// </summary>
public class QueryExecutionResult
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public long ExecutionDurationMs { get; set; }
}
