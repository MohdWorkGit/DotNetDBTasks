namespace Bayan.Application.Common.Models;

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

    /// <summary>
    /// True when this result is a preview of a write query (INSERT/UPDATE/DELETE).
    /// The transaction has been rolled back; the client must re-submit with confirmation
    /// to actually commit the changes. AffectedRows reflects the count that would be changed.
    /// </summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>
    /// For preview results on UPDATE/DELETE: the current rows in the database that match the
    /// WHERE clause and will be modified or deleted. Empty when the SQL cannot be parsed or
    /// for INSERT statements.
    /// </summary>
    public List<string> PreviewColumns { get; set; } = new();
    public List<Dictionary<string, object?>> PreviewRows { get; set; } = new();
}
