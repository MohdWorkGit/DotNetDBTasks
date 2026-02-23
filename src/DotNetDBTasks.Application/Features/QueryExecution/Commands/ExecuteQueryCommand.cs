using System.Diagnostics;
using System.Text.Json;
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

        // Verify user has access via roles
        var userRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == _currentUser.UserId, cancellationToken);
        var userRoleIds = userRoles.Select(ur => ur.RoleId).ToHashSet();

        var queryRoles = await _unitOfWork.DynamicQueryRoles.FindAsync(
            qr => qr.DynamicQueryId == request.QueryId, cancellationToken);
        var hasAccess = queryRoles.Any(qr => userRoleIds.Contains(qr.RoleId));

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

        var log = new QueryExecutionLog
        {
            Id = Guid.NewGuid(),
            DynamicQueryId = request.QueryId,
            UserId = _currentUser.UserId,
            ParametersJson = JsonSerializer.Serialize(request.Parameters),
            ExecutedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _queryExecutor.ExecuteAsync(
                query.SqlQuery, typedParameters, query.TimeoutSeconds, cancellationToken);

            sw.Stop();
            log.ExecutionDurationMs = sw.ElapsedMilliseconds;
            log.RowsReturned = result.TotalRows;
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
            _ => rawValue
        };
    }
}
