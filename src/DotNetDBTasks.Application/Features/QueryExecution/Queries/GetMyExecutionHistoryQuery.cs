using AutoMapper;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.QueryExecution.Queries;

/// <summary>
/// Retrieves the current user's query execution history.
/// </summary>
public record GetMyExecutionHistoryQuery : IRequest<IReadOnlyList<ExecutionLogDto>>;

public class GetMyExecutionHistoryQueryHandler
    : IRequestHandler<GetMyExecutionHistoryQuery, IReadOnlyList<ExecutionLogDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;

    public GetMyExecutionHistoryQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<ExecutionLogDto>> Handle(
        GetMyExecutionHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var logs = await _unitOfWork.QueryExecutionLogs.FindAsync(
            l => l.UserId == _currentUser.UserId, cancellationToken);

        return _mapper.Map<IReadOnlyList<ExecutionLogDto>>(
            logs.OrderByDescending(l => l.ExecutedAt).ToList());
    }
}
