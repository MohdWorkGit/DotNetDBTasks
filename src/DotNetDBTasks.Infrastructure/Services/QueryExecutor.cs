using System.Diagnostics;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DotNetDBTasks.Infrastructure.Services;

/// <summary>
/// Executes parameterized SQL queries securely against SQL Server.
/// All parameters are passed through SqlParameter — no string concatenation.
/// </summary>
public class QueryExecutor : IQueryExecutor
{
    private readonly string _connectionString;

    public QueryExecutor(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not configured.");
    }

    public async Task<QueryExecutionResult> ExecuteAsync(
        string sqlQuery,
        Dictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var result = new QueryExecutionResult();
        var sw = Stopwatch.StartNew();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sqlQuery;
        command.CommandTimeout = timeoutSeconds;

        // All parameters are added via SqlParameter — strict parameterization
        foreach (var param in parameters)
        {
            command.Parameters.Add(new SqlParameter($"@{param.Key}", param.Value ?? DBNull.Value));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // Read column names
        for (int i = 0; i < reader.FieldCount; i++)
        {
            result.Columns.Add(reader.GetName(i));
        }

        // Read rows
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                row[result.Columns[i]] = value == DBNull.Value ? null : value;
            }
            result.Rows.Add(row);
        }

        sw.Stop();
        result.TotalRows = result.Rows.Count;
        result.ExecutionDurationMs = sw.ElapsedMilliseconds;

        return result;
    }
}
