using System.Linq.Expressions;
using System.Text.Json;
using DotNetDBTasks.Domain.Entities;

namespace DotNetDBTasks.Application.Features.QueryExecution.Queries;

/// <summary>
/// Database projection target for execution-log list queries. Deliberately excludes
/// OldValuesJson, which can hold thousands of row snapshots per log — loading it for
/// every listed log is what used to drive memory into the gigabytes.
/// </summary>
public sealed class ExecutionLogRow
{
    public Guid Id { get; init; }
    public Guid DynamicQueryId { get; init; }
    public string QueryName { get; init; } = string.Empty;
    public string SqlQuery { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string? ParametersJson { get; init; }
    /// <summary>
    /// 1 when OldValuesJson is present, else 0. An int rather than a bool because the
    /// Oracle EF provider renders computed boolean projections as TRUE/FALSE literals,
    /// which ORA-00904 rejects.
    /// </summary>
    public int HasOldValuesFlag { get; init; }
    public DateTime ExecutedAt { get; init; }
    public long ExecutionDurationMs { get; init; }
    public int RowsReturned { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}

public static class ExecutionLogQueryHelper
{
    public const int MaxPageSize = 200;

    public static readonly Expression<Func<QueryExecutionLog, ExecutionLogRow>> Projection =
        l => new ExecutionLogRow
        {
            Id = l.Id,
            DynamicQueryId = l.DynamicQueryId,
            QueryName = l.DynamicQuery.Name,
            SqlQuery = l.DynamicQuery.SqlQuery,
            UserId = l.UserId,
            Username = l.User.Username,
            ParametersJson = l.ParametersJson,
            HasOldValuesFlag = l.OldValuesJson != null ? 1 : 0,
            ExecutedAt = l.ExecutedAt,
            ExecutionDurationMs = l.ExecutionDurationMs,
            RowsReturned = l.RowsReturned,
            IsSuccess = l.IsSuccess,
            ErrorMessage = l.ErrorMessage
        };

    public static Expression<Func<QueryExecutionLog, object>> GetOrderBy(string? sortBy) =>
        sortBy switch
        {
            "queryName" => l => l.DynamicQuery.Name,
            "username" => l => l.User.Username,
            "executionDurationMs" => l => l.ExecutionDurationMs,
            "rowsReturned" => l => l.RowsReturned,
            "isSuccess" => l => l.IsSuccess,
            _ => l => l.ExecutedAt
        };

    public static ExecutionLogDto ToDto(ExecutionLogRow row) => new()
    {
        Id = row.Id,
        DynamicQueryId = row.DynamicQueryId,
        QueryName = row.QueryName,
        UserId = row.UserId,
        Username = row.Username,
        Parameters = ParseParameters(row.ParametersJson),
        HasOldValues = row.HasOldValuesFlag == 1,
        IsUpdateQuery = StartsWithKeyword(row.SqlQuery, "UPDATE"),
        IsDeleteQuery = StartsWithKeyword(row.SqlQuery, "DELETE"),
        ExecutedAt = row.ExecutedAt,
        ExecutionDurationMs = row.ExecutionDurationMs,
        RowsReturned = row.RowsReturned,
        IsSuccess = row.IsSuccess,
        ErrorMessage = row.ErrorMessage
    };

    public static int ClampPageNumber(int pageNumber) => pageNumber < 1 ? 1 : pageNumber;

    public static int ClampPageSize(int pageSize) =>
        pageSize < 1 ? 25 : Math.Min(pageSize, MaxPageSize);

    private static Dictionary<string, string> ParseParameters(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static bool StartsWithKeyword(string sql, string keyword) =>
        sql.TrimStart().StartsWith(keyword, StringComparison.OrdinalIgnoreCase);
}
