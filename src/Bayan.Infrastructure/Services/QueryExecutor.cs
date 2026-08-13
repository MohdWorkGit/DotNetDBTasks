using System.Collections;
using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace Bayan.Infrastructure.Services;

/// <summary>
/// Executes parameterized SQL queries securely against Oracle, SQL Server, PostgreSQL, and MySQL.
/// All parameters are passed through DbParameter — no string concatenation.
/// </summary>
public class QueryExecutor : IQueryExecutor
{
    private readonly string _defaultConnectionString;
    private readonly DatabaseServerType _defaultServerType;
    private readonly ISystemSettingsService _settings;

    /// <summary>
    /// The row cap from appsettings.json. It is the <em>default</em> the runtime setting falls
    /// back to, not a competing value: an installation that already tuned MaxQueryRows keeps
    /// its number until someone changes it on the Settings page.
    /// </summary>
    private readonly int _configuredMaxQueryRows;

    public QueryExecutor(IConfiguration configuration, ISystemSettingsService settings)
    {
        _settings = settings;
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not configured.");

        var serverTypeStr = configuration["DefaultDatabaseServerType"];
        _defaultServerType = Enum.TryParse<DatabaseServerType>(serverTypeStr, true, out var parsed)
            ? parsed
            : DatabaseServerType.Oracle;

        _configuredMaxQueryRows = configuration.GetValue(
            "MaxQueryRows", SystemSettingKeys.QueryMaxRowsDefault);
    }

    /// <summary>The cap in force right now: the runtime setting, or the configured default.</summary>
    private Task<int> ResolveMaxRowsAsync(CancellationToken cancellationToken) =>
        _settings.GetIntAsync(SystemSettingKeys.QueryMaxRows, _configuredMaxQueryRows, cancellationToken);

    public async Task<QueryExecutionResult> ExecuteAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var maxRows = await ResolveMaxRowsAsync(cancellationToken);
        return await ExecuteInternalAsync(sqlQuery, parameters, timeoutSeconds, _defaultConnectionString, _defaultServerType, maxRows, cancellationToken);
    }

    public async Task<QueryExecutionResult> ExecuteAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        string connectionString,
        DatabaseServerType serverType,
        CancellationToken cancellationToken = default)
    {
        var maxRows = await ResolveMaxRowsAsync(cancellationToken);
        return await ExecuteInternalAsync(sqlQuery, parameters, timeoutSeconds, connectionString, serverType, maxRows, cancellationToken);
    }

    public Task<QueryExecutionResult> ExecuteAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        string? connectionString,
        DatabaseServerType? serverType,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        var cs = connectionString ?? _defaultConnectionString;
        var st = serverType ?? _defaultServerType;
        return ExecuteInternalAsync(sqlQuery, parameters, timeoutSeconds, cs, st, maxRows, cancellationToken);
    }

    public Task<QueryExecutionResult> ExecutePreviewAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        string? connectionString,
        DatabaseServerType? serverType,
        CancellationToken cancellationToken = default)
    {
        var cs = connectionString ?? _defaultConnectionString;
        var st = serverType ?? _defaultServerType;
        return ExecutePreviewInternalAsync(sqlQuery, parameters, timeoutSeconds, cs, st, cancellationToken);
    }

    private static async Task<QueryExecutionResult> ExecutePreviewInternalAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        string connectionString,
        DatabaseServerType serverType,
        CancellationToken cancellationToken)
    {
        var result = new QueryExecutionResult();
        var sw = Stopwatch.StartNew();

        var (expandedSql, expandedParameters) = ExpandCollectionParameters(sqlQuery, parameters);
        var adaptedSql = AdaptSqlSyntax(expandedSql, serverType);

        await using var connection = CreateConnection(connectionString, serverType);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = adaptedSql;
            command.CommandTimeout = timeoutSeconds;

            if (command is OracleCommand oracleCmd)
            {
                oracleCmd.BindByName = true;
            }

            AddParameters(command, expandedParameters, serverType);

            try
            {
                var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
                result.AffectedRows = affectedRows;
            }
            catch (Exception ex) when (IsCommandTimeout(ex, cancellationToken))
            {
                throw new QueryTimeoutException(timeoutSeconds, ex);
            }
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        sw.Stop();
        result.ExecutionDurationMs = sw.ElapsedMilliseconds;
        result.RequiresConfirmation = true;

        return result;
    }

    private static async Task<QueryExecutionResult> ExecuteInternalAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        string connectionString,
        DatabaseServerType serverType,
        int maxQueryRows,
        CancellationToken cancellationToken)
    {
        var result = new QueryExecutionResult();
        var sw = Stopwatch.StartNew();

        var (expandedSql, expandedParameters) = ExpandCollectionParameters(sqlQuery, parameters);
        var adaptedSql = AdaptSqlSyntax(expandedSql, serverType);

        await using var connection = CreateConnection(connectionString, serverType);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = adaptedSql;
        command.CommandTimeout = timeoutSeconds;

        // Oracle-specific: enable bind-by-name
        if (command is OracleCommand oracleCmd)
        {
            oracleCmd.BindByName = true;
        }

        AddParameters(command, expandedParameters, serverType);

        try
        {
            if (IsSelectQuery(sqlQuery))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    result.Columns.Add(reader.GetName(i));
                }

                while (await reader.ReadAsync(cancellationToken))
                {
                    if (result.Rows.Count >= maxQueryRows)
                    {
                        result.IsLimitReached = true;
                        break;
                    }

                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.GetValue(i);
                        row[result.Columns[i]] = value == DBNull.Value ? null : value;
                    }
                    result.Rows.Add(row);
                }

                result.TotalRows = result.Rows.Count;
            }
            else
            {
                var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
                result.AffectedRows = affectedRows;
            }
        }
        catch (Exception ex) when (IsCommandTimeout(ex, cancellationToken))
        {
            throw new QueryTimeoutException(timeoutSeconds, ex);
        }

        sw.Stop();
        result.ExecutionDurationMs = sw.ElapsedMilliseconds;

        return result;
    }

    /// <summary>
    /// True when the exception is how the database driver reports an expired
    /// CommandTimeout. Drivers surface it as a generic cancellation (Oracle raises
    /// ORA-01013 or a bare "task was canceled"), which reads as if someone cancelled
    /// the query. A genuine cancellation — the caller's token actually fired — is
    /// never classified as a timeout.
    /// </summary>
    private static bool IsCommandTimeout(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        return ex switch
        {
            // The token didn't fire, so the only thing that cancelled is the driver's timeout.
            OperationCanceledException => true,
            TimeoutException => true,
            OracleException oracleEx => oracleEx.Number == 1013,  // ORA-01013: operation cancelled (timeout)
            SqlException sqlEx => sqlEx.Number == -2,             // execution timeout expired
            MySqlException mySqlEx => mySqlEx.ErrorCode == MySqlErrorCode.CommandTimeoutExpired,
            NpgsqlException npgsqlEx => npgsqlEx.InnerException is TimeoutException,
            _ => false
        };
    }

    /// <summary>
    /// Creates the appropriate DbConnection for the given server type.
    /// </summary>
    private static DbConnection CreateConnection(string connectionString, DatabaseServerType serverType)
    {
        return serverType switch
        {
            DatabaseServerType.Oracle => new OracleConnection(connectionString),
            DatabaseServerType.SqlServer => new SqlConnection(connectionString),
            DatabaseServerType.PostgreSql => new NpgsqlConnection(connectionString),
            DatabaseServerType.MySql => new MySqlConnection(connectionString),
            _ => throw new NotSupportedException($"Database server type '{serverType}' is not supported.")
        };
    }

    /// <summary>
    /// Expands collection-valued parameters into multiple scalar bind variables so
    /// `WHERE col IN (@names)` becomes `WHERE col IN (@names_0, @names_1, ...)` with
    /// each item bound separately. The user's SQL must already wrap the parameter in
    /// the IN clause's own parens — we only inject the comma list, never an extra
    /// pair, because Oracle (and the SQL spec) treats `IN ((a, b, c))` as a row-
    /// constructor compared against a scalar (ORA-00907). Empty collections expand
    /// to `NULL` so the resulting `IN (NULL)` is valid SQL that matches no rows.
    ///
    /// Non-mutating: returns a fresh dictionary so callers that reuse the original
    /// parameters across multiple executions (e.g. preview + affected-rows fetch)
    /// still see the original collection entries.
    /// </summary>
    private static (string Sql, Dictionary<string, object?> Parameters) ExpandCollectionParameters(
        string sql, Dictionary<string, object?> parameters)
    {
        var expanded = new Dictionary<string, object?>(parameters.Count);

        foreach (var entry in parameters)
        {
            if (!IsExpandableCollection(entry.Value))
            {
                expanded[entry.Key] = entry.Value;
                continue;
            }

            var items = ((IEnumerable)entry.Value!).Cast<object?>().ToList();
            // `\b` after the name prevents matching `@names_extra` or `@namesId` when
            // expanding `@names` — the next char must be a non-word boundary.
            var placeholderPattern = $@"@{Regex.Escape(entry.Key)}\b";

            if (items.Count == 0)
            {
                sql = Regex.Replace(sql, placeholderPattern, "NULL");
                continue;
            }

            var expandedKeys = Enumerable.Range(0, items.Count)
                .Select(i => $"{entry.Key}_{i}")
                .ToArray();

            var placeholders = string.Join(", ", expandedKeys.Select(k => "@" + k));
            sql = Regex.Replace(sql, placeholderPattern, placeholders);

            for (int i = 0; i < items.Count; i++)
            {
                expanded[expandedKeys[i]] = items[i] ?? DBNull.Value;
            }
        }

        return (sql, expanded);
    }

    private static bool IsExpandableCollection(object? value) =>
        value is IEnumerable && value is not string && value is not byte[];

    /// <summary>
    /// Adapts SQL syntax for the target database server type.
    /// Queries are stored with @param syntax and [bracket] quoting.
    /// </summary>
    private static string AdaptSqlSyntax(string sql, DatabaseServerType serverType)
    {
        return serverType switch
        {
            DatabaseServerType.Oracle => AdaptForOracle(sql),
            DatabaseServerType.SqlServer => sql, // SQL Server natively supports @param and [bracket] syntax
            DatabaseServerType.PostgreSql => AdaptForPostgreSql(sql),
            DatabaseServerType.MySql => AdaptForMySql(sql),
            _ => sql
        };
    }

    private static string AdaptForOracle(string sql)
    {
        // Convert [bracket] quoting to "double-quote" quoting
        var adapted = Regex.Replace(sql, @"\[([^\]]+)\]", "\"$1\"");
        // Convert @paramName to :paramName bind variable syntax
        adapted = Regex.Replace(adapted, @"@(\w+)", ":$1");
        return adapted;
    }

    private static string AdaptForPostgreSql(string sql)
    {
        // Convert [bracket] quoting to "double-quote" quoting
        var adapted = Regex.Replace(sql, @"\[([^\]]+)\]", "\"$1\"");
        return adapted;
    }

    private static string AdaptForMySql(string sql)
    {
        // Convert [bracket] quoting to `backtick` quoting
        var adapted = Regex.Replace(sql, @"\[([^\]]+)\]", "`$1`");
        return adapted;
    }

    /// <summary>
    /// Adds parameters using the appropriate DbParameter type for each server type.
    /// </summary>
    private static void AddParameters(DbCommand command, Dictionary<string, object?> parameters, DatabaseServerType serverType)
    {
        foreach (var param in parameters)
        {
            var dbParam = serverType switch
            {
                DatabaseServerType.Oracle => new OracleParameter($":{param.Key}", param.Value ?? DBNull.Value) as DbParameter,
                DatabaseServerType.SqlServer => new SqlParameter($"@{param.Key}", param.Value ?? DBNull.Value),
                DatabaseServerType.PostgreSql => new NpgsqlParameter($"@{param.Key}", param.Value ?? DBNull.Value),
                DatabaseServerType.MySql => new MySqlParameter($"@{param.Key}", param.Value ?? DBNull.Value),
                _ => throw new NotSupportedException($"Database server type '{serverType}' is not supported.")
            };
            command.Parameters.Add(dbParam);
        }
    }

    /// <summary>
    /// Determines if a SQL statement returns a result set.
    /// SELECT and WITH (CTE) queries return rows; all others are non-query statements.
    /// </summary>
    private static bool IsSelectQuery(string sql)
    {
        var trimmed = sql.TrimStart();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }
}
