using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.QueryExecution.Commands;

/// <summary>
/// Executes a dynamic query with user-provided parameters.
/// Enforces strict parameterization — no string concatenation.
/// </summary>
public class ExecuteQueryCommand : IRequest<QueryExecutionResult>
{
    public Guid QueryId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class ExecuteQueryCommandHandler : IRequestHandler<ExecuteQueryCommand, QueryExecutionResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IQueryExecutor _queryExecutor;
    private readonly ICurrentUserService _currentUser;

    public ExecuteQueryCommandHandler(
        IUnitOfWork unitOfWork,
        IQueryExecutor queryExecutor,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _queryExecutor = queryExecutor;
        _currentUser = currentUser;
    }

    public async Task<QueryExecutionResult> Handle(
        ExecuteQueryCommand request,
        CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken);
        if (query is null)
            throw new NotFoundException(nameof(DynamicQuery), request.QueryId);

        if (!query.IsEnabled)
            throw new DomainException("This query is currently disabled.");

        // Verify user has access via roles, department, or direct user assignment
        var hasAccess = false;

        // Check role-based access
        var userRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == _currentUser.UserId, cancellationToken);
        var userRoleIds = userRoles.Select(ur => ur.RoleId).ToHashSet();

        var queryRoles = await _unitOfWork.DynamicQueryRoles.FindAsync(
            qr => qr.DynamicQueryId == request.QueryId, cancellationToken);
        hasAccess = queryRoles.Any(qr => userRoleIds.Contains(qr.RoleId));

        // Check department-based access
        if (!hasAccess && !string.IsNullOrEmpty(_currentUser.Department))
        {
            hasAccess = await _unitOfWork.DynamicQueryDepartments.ExistsAsync(
                qd => qd.DynamicQueryId == request.QueryId && qd.Department == _currentUser.Department,
                cancellationToken);
        }

        // Check direct user assignment
        if (!hasAccess)
        {
            hasAccess = await _unitOfWork.DynamicQueryUsers.ExistsAsync(
                qu => qu.DynamicQueryId == request.QueryId && qu.UserId == _currentUser.UserId,
                cancellationToken);
        }

        // Admins always have access
        if (!hasAccess && !_currentUser.Roles.Contains("Admin"))
            throw new ForbiddenAccessException("You do not have access to this query.");

        // Build typed parameters from metadata
        var queryParams = await _unitOfWork.QueryParameters.FindAsync(
            p => p.DynamicQueryId == request.QueryId, cancellationToken);

        var typedParameters = new Dictionary<string, object?>();
        foreach (var paramDef in queryParams)
        {
            request.Parameters.TryGetValue(paramDef.Name, out var rawValue);

            if (paramDef.IsRequired && string.IsNullOrWhiteSpace(rawValue))
                throw new DomainException($"Parameter '{paramDef.DisplayName}' is required.");

            typedParameters[paramDef.Name] = ConvertParameter(rawValue, paramDef.ParameterType);
        }

        // For UPDATE queries, fetch the current (old) values from the database before applying the update.
        string? oldValuesJson = null;
        if (IsUpdateQuery(query.SqlQuery))
        {
            oldValuesJson = await FetchOldValuesJsonAsync(
                query.SqlQuery, typedParameters, query.TimeoutSeconds, cancellationToken);
        }

        var log = new QueryExecutionLog
        {
            Id = Guid.NewGuid(),
            DynamicQueryId = request.QueryId,
            UserId = _currentUser.UserId,
            ParametersJson = JsonSerializer.Serialize(request.Parameters),
            OldValuesJson = oldValuesJson,
            ExecutedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _queryExecutor.ExecuteAsync(
                query.SqlQuery, typedParameters, query.TimeoutSeconds, cancellationToken);

            sw.Stop();
            result.Parameters = typedParameters;
            log.ExecutionDurationMs = sw.ElapsedMilliseconds;
            log.RowsReturned = result.TotalRows > 0 ? result.TotalRows : result.AffectedRows;
            log.IsSuccess = true;

            await _unitOfWork.QueryExecutionLogs.AddAsync(log, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            log.ExecutionDurationMs = sw.ElapsedMilliseconds;
            log.IsSuccess = false;
            log.ErrorMessage = ex.Message;

            await _unitOfWork.QueryExecutionLogs.AddAsync(log, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw;
        }
    }

    private static object? ConvertParameter(string? rawValue, ParameterType type)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return DBNull.Value;

        return type switch
        {
            ParameterType.String => rawValue,
            ParameterType.Number => decimal.TryParse(rawValue, out var num) ? num
                : throw new DomainException($"Invalid number value: {rawValue}"),
            ParameterType.Date => DateTime.TryParse(rawValue, out var date) ? date
                : throw new DomainException($"Invalid date value: {rawValue}"),
            // Oracle NUMBER(1) columns require 0/1 integers, not .NET bool values
            ParameterType.Boolean => bool.TryParse(rawValue, out var flag) ? (flag ? 1 : 0)
                : throw new DomainException($"Invalid boolean value: {rawValue}"),
            // Dropdown value is passed as a plain string (the selected option's value)
            ParameterType.Dropdown => rawValue,
            _ => rawValue
        };
    }

    private static bool IsUpdateQuery(string sql) =>
        sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Attempts to build a SELECT from the UPDATE statement to read current column values
    /// before the update is applied. Returns serialized JSON of the first matching row,
    /// or null if the SQL cannot be parsed or the pre-SELECT fails.
    ///
    /// Supports the standard single-table UPDATE pattern:
    ///   UPDATE table SET col1 = @p1 [, col2 = @p2 ...] WHERE condition
    /// </summary>
    private async Task<string?> FetchOldValuesJsonAsync(
        string sql,
        Dictionary<string, object?> typedParameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse: UPDATE <table> SET <setClause> WHERE <whereClause>
            var match = Regex.Match(
                sql.Trim(),
                @"UPDATE\s+(\w+)\s+SET\s+(.*?)\s+WHERE\s+(.*)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!match.Success) return null;

            var tableName = match.Groups[1].Value;
            var setPart   = match.Groups[2].Value;
            var wherePart = match.Groups[3].Value;

            // Extract column names from the SET clause: col = @param  →  col
            var setColumns = Regex.Matches(setPart, @"(\w+)\s*=\s*@\w+")
                .Select(m => m.Groups[1].Value)
                .ToList();

            // Extract parameter names referenced in the WHERE clause: @param  →  param
            var whereParamNames = Regex.Matches(wherePart, @"@(\w+)")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (setColumns.Count == 0 || whereParamNames.Count == 0) return null;

            // Build the lookup SELECT using the same WHERE clause
            var selectSql = $"SELECT {string.Join(", ", setColumns)} FROM {tableName} WHERE {wherePart}";

            // Only pass parameters that appear in the WHERE clause
            var whereParams = typedParameters
                .Where(p => whereParamNames.Contains(p.Key))
                .ToDictionary(p => p.Key, p => p.Value);

            var result = await _queryExecutor.ExecuteAsync(selectSql, whereParams, timeoutSeconds, cancellationToken);

            if (result.Rows.Count == 0) return null;

            var oldValues = new Dictionary<string, string>();
            foreach (var col in result.Columns)
            {
                oldValues[col] = result.Rows[0][col]?.ToString() ?? "NULL";
            }

            return JsonSerializer.Serialize(oldValues);
        }
        catch
        {
            // Never let a pre-fetch failure block the actual query execution
            return null;
        }
    }
}
