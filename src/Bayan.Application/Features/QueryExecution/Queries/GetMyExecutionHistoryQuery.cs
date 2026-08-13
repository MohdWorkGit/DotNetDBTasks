using System.Linq.Expressions;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;
using Bayan.Domain.Entities;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.QueryExecution.Queries;

/// <summary>
/// Retrieves one page of the current user's query execution history. Paging and sorting run
/// in the database, and the projection never touches OldValuesJson.
/// </summary>
public class GetMyExecutionHistoryQuery : IRequest<PaginatedList<ExecutionLogDto>>
{
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class GetMyExecutionHistoryQueryHandler
    : IRequestHandler<GetMyExecutionHistoryQuery, PaginatedList<ExecutionLogDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public GetMyExecutionHistoryQueryHandler(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<PaginatedList<ExecutionLogDto>> Handle(
        GetMyExecutionHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        Expression<Func<QueryExecutionLog, bool>> predicate = l => l.UserId == userId;

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
