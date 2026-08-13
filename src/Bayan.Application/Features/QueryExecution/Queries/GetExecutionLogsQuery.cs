using System.Linq.Expressions;
using Bayan.Application.Common.Models;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.QueryExecution.Queries;

/// <summary>
/// Retrieves one page of execution logs for the admin/auditor view. Filtering, sorting and
/// paging all run in the database, and the projection never touches OldValuesJson.
/// </summary>
public class GetExecutionLogsQuery : IRequest<PaginatedList<ExecutionLogDto>>
{
    public Guid? QueryId { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>Optional status filter: true = success only, false = failed only.</summary>
    public bool? IsSuccess { get; set; }
    /// <summary>Optional filter on the query's statement type (SELECT/INSERT/UPDATE/DELETE/other).</summary>
    public QueryType? QueryType { get; set; }
    /// <summary>Case-insensitive text filter over query name, username, parameters and error.</summary>
    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class GetExecutionLogsQueryHandler
    : IRequestHandler<GetExecutionLogsQuery, PaginatedList<ExecutionLogDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetExecutionLogsQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PaginatedList<ExecutionLogDto>> Handle(
        GetExecutionLogsQuery request,
        CancellationToken cancellationToken)
    {
        var queryId = request.QueryId;
        var userId = request.UserId;
        var isSuccess = request.IsSuccess;
        var queryType = request.QueryType;
        var search = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim().ToUpperInvariant();

        Expression<Func<QueryExecutionLog, bool>> predicate = l =>
            (queryId == null || l.DynamicQueryId == queryId) &&
            (userId == null || l.UserId == userId) &&
            (isSuccess == null || l.IsSuccess == isSuccess) &&
            (queryType == null || l.DynamicQuery.QueryType == queryType) &&
            (search == null ||
                l.DynamicQuery.Name.ToUpper().Contains(search) ||
                l.User.Username.ToUpper().Contains(search) ||
                (l.ErrorMessage != null && l.ErrorMessage.ToUpper().Contains(search)) ||
                (l.ParametersJson != null && l.ParametersJson.ToUpper().Contains(search)));

        var pageNumber = ExecutionLogQueryHelper.ClampPageNumber(request.PageNumber);
        var pageSize = ExecutionLogQueryHelper.ClampPageSize(request.PageSize);

        var (rows, totalCount) = await _unitOfWork.QueryExecutionLogs.GetPagedAsync(
            predicate,
            ExecutionLogQueryHelper.GetOrderBy(request.SortBy),
            request.SortDescending,
            pageNumber,
            pageSize,
            ExecutionLogQueryHelper.Projection,
            cancellationToken);

        return new PaginatedList<ExecutionLogDto>(
            rows.Select(ExecutionLogQueryHelper.ToDto).ToList(),
            totalCount, pageNumber, pageSize);
    }
}
