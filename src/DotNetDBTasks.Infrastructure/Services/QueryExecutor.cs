using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace DotNetDBTasks.Infrastructure.Services;

/// <summary>
/// Executes parameterized SQL queries securely against Oracle, SQL Server, PostgreSQL, and MySQL.
/// All parameters are passed through DbParameter — no string concatenation.
/// </summary>
public class QueryExecutor : IQueryExecutor
{
    private readonly string _defaultConnectionString;
    private readonly DatabaseServerType _defaultServerType;
    private readonly int _maxQueryRows;

    public QueryExecutor(IConfiguration configuration)
    {
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not configured.");

        var serverTypeStr = configuration["DefaultDatabaseServerType"];
        _defaultServerType = Enum.TryParse<DatabaseServerType>(serverTypeStr, true, out var parsed)
            ? parsed
            : DatabaseServerType.Oracle;

        _maxQueryRows = configuration.GetValue<int>("MaxQueryRows", 10_000);
    }

    public Task<QueryExecutionResult> ExecuteAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInternalAsync(sqlQuery, parameters, timeoutSeconds, _defaultConnectionString, _defaultServerType, _maxQueryRows, cancellationToken);
    }

    public Task<QueryExecutionResult> ExecuteAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        string connectionString,
        DatabaseServerType serverType,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInternalAsync(sqlQuery, parameters, timeoutSeconds, connectionString, serverType, _maxQueryRows, cancellationToken);
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

        var adaptedSql = AdaptSqlSyntax(sqlQuery, serverType);

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

        AddParameters(command, parameters, serverType);

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

        sw.Stop();
        result.ExecutionDurationMs = sw.ElapsedMilliseconds;

        return result;
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
