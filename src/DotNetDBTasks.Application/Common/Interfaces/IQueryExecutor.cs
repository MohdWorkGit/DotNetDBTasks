using DotNetDBTasks.Application.Common.Models;

namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Executes parameterized SQL queries against the database securely.
/// All parameters must be passed through proper parameterization — no string concatenation allowed.
/// </summary>
public interface IQueryExecutor
{
    Task<QueryExecutionResult> ExecuteAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a query using a specific connection string (for dynamic database user support).
    /// </summary>
    Task<QueryExecutionResult> ExecuteAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        string connectionString,
        CancellationToken cancellationToken = default);
}
