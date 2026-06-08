using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Domain.Enums;

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
    /// Executes a query using a specific connection string and database server type.
    /// </summary>
    Task<QueryExecutionResult> ExecuteAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        string connectionString,
        DatabaseServerType serverType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a query with an explicit row cap, overriding the configured default.
    /// Pass <see cref="int.MaxValue"/> for an effectively unlimited result set (used by export).
    /// A null connection string / server type falls back to the configured default database.
    /// </summary>
    Task<QueryExecutionResult> ExecuteAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        string? connectionString,
        DatabaseServerType? serverType,
        int maxRows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a non-SELECT query inside a transaction and rolls back, returning the
    /// number of rows that would be affected if committed.
    /// </summary>
    Task<QueryExecutionResult> ExecutePreviewAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        string? connectionString,
        DatabaseServerType? serverType,
        CancellationToken cancellationToken = default);
}
