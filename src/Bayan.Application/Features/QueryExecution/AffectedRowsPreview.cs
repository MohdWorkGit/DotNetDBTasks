using System.Text.RegularExpressions;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;
using Bayan.Domain.Enums;

namespace Bayan.Application.Features.QueryExecution;

/// <summary>
/// The rows an UPDATE or DELETE would touch, read with a SELECT over the same WHERE clause.
/// Shared by a real run's preview/old-values capture and a test run from the query editor.
/// </summary>
public static class AffectedRowsPreview
{
    /// <summary>
    /// For UPDATE/DELETE statements, parses out the table and WHERE clause and runs a
    /// SELECT * against the same predicate so the user can see which rows will be affected
    /// before confirming. Returns null for INSERT or when the SQL can't be parsed.
    /// </summary>
    public static async Task<QueryExecutionResult?> FetchAsync(
        IQueryExecutor queryExecutor,
        string sql,
        Dictionary<string, object?> typedParameters,
        int timeoutSeconds,
        string? connectionString,
        DatabaseServerType? serverType,
        CancellationToken cancellationToken)
    {
        try
        {
            var trimmed = sql.TrimStart();
            string? tableName = null;
            string? wherePart = null;

            // Identifier: bare word, "double-quoted", or [bracketed], optionally schema-qualified
            // (each segment may use any of the three forms). Captured group includes an optional
            // table alias so it can be embedded directly into the SELECT's FROM clause —
            // preserving the alias is required when the WHERE clause references it.
            const string ident = @"(?:""[^""]+""|\[[^\]]+\]|\w+)(?:\.(?:""[^""]+""|\[[^\]]+\]|\w+))?";

            if (trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(
                    trimmed,
                    $@"UPDATE\s+({ident}(?:\s+(?:AS\s+)?(?!SET\b)\w+)?)\s+SET\s+.*?\s+WHERE\s+(.*)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    tableName = match.Groups[1].Value;
                    wherePart = match.Groups[2].Value;
                }
            }
            else if (trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(
                    trimmed,
                    $@"DELETE\s+FROM\s+({ident}(?:\s+(?:AS\s+)?(?!WHERE\b)\w+)?)(?:\s+WHERE\s+(.*))?",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    tableName = match.Groups[1].Value;
                    wherePart = match.Groups[2].Success ? match.Groups[2].Value : null;
                }
            }
            else
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(tableName)) return null;

            var selectSql = string.IsNullOrWhiteSpace(wherePart)
                ? $"SELECT * FROM {tableName}"
                : $"SELECT * FROM {tableName} WHERE {wherePart}";

            Dictionary<string, object?> selectParams;
            if (string.IsNullOrWhiteSpace(wherePart))
            {
                selectParams = new Dictionary<string, object?>();
            }
            else
            {
                var whereParamNames = Regex.Matches(wherePart, @"[@:](\w+)")
                    .Select(m => m.Groups[1].Value)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                selectParams = typedParameters
                    .Where(p => whereParamNames.Contains(p.Key))
                    .ToDictionary(p => p.Key, p => p.Value);
            }

            if (connectionString != null && serverType != null)
            {
                return await queryExecutor.ExecuteAsync(
                    selectSql, selectParams, timeoutSeconds, connectionString, serverType.Value, cancellationToken);
            }
            return await queryExecutor.ExecuteAsync(selectSql, selectParams, timeoutSeconds, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
