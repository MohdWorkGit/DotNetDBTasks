using System.Text.Json;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.QueryExecution.Queries;

/// <summary>
/// Retrieves one page of pre-change row snapshots (OldValuesJson) for a single execution
/// log. Only one log's JSON blob is ever loaded, and only the requested page of rows is
/// materialized and returned — this keeps memory flat even for logs covering thousands
/// of affected rows.
/// </summary>
public class GetExecutionLogOldValuesQuery : IRequest<ExecutionLogOldValuesPageDto>
{
    public Guid LogId { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    /// <summary>
    /// When true (user-facing history endpoint), the log must belong to the current user;
    /// other users' logs are reported as not found.
    /// </summary>
    public bool RestrictToCurrentUser { get; set; }
}

public class GetExecutionLogOldValuesQueryHandler
    : IRequestHandler<GetExecutionLogOldValuesQuery, ExecutionLogOldValuesPageDto>
{
    private const int MaxPageSize = 500;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public GetExecutionLogOldValuesQueryHandler(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<ExecutionLogOldValuesPageDto> Handle(
        GetExecutionLogOldValuesQuery request,
        CancellationToken cancellationToken)
    {
        var log = await _unitOfWork.QueryExecutionLogs.GetByIdAsync(request.LogId, cancellationToken);
        if (log is null || (request.RestrictToCurrentUser && log.UserId != _currentUser.UserId))
            throw new NotFoundException(nameof(QueryExecutionLog), request.LogId);

        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize = request.PageSize < 1 ? 100 : Math.Min(request.PageSize, MaxPageSize);

        var result = new ExecutionLogOldValuesPageDto
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        if (string.IsNullOrEmpty(log.OldValuesJson))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(log.OldValuesJson);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                result.TotalRows = root.GetArrayLength();
                var skip = (pageNumber - 1) * pageSize;
                foreach (var element in root.EnumerateArray().Skip(skip).Take(pageSize))
                    result.Rows.Add(ToRow(element, result.Columns));
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Legacy format: a single row stored as one object.
                result.TotalRows = 1;
                if (pageNumber == 1)
                    result.Rows.Add(ToRow(root, result.Columns));
            }
        }
        catch (JsonException)
        {
            // Malformed legacy payload — treat as having no recoverable rows.
        }

        return result;
    }

    private static Dictionary<string, string> ToRow(JsonElement element, List<string> columns)
    {
        var row = new Dictionary<string, string>();
        if (element.ValueKind != JsonValueKind.Object)
            return row;

        foreach (var property in element.EnumerateObject())
        {
            row[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.ToString();
            if (!columns.Contains(property.Name))
                columns.Add(property.Name);
        }
        return row;
    }
}
