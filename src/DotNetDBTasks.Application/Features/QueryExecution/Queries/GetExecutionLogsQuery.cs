using AutoMapper;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.QueryExecution.Queries;

/// <summary>
/// Retrieves execution logs. Admin sees all; user sees own.
/// </summary>
public class GetExecutionLogsQuery : IRequest<IReadOnlyList<ExecutionLogDto>>
{
    public Guid? QueryId { get; set; }
    public Guid? UserId { get; set; }
}

public class GetExecutionLogsQueryHandler
    : IRequestHandler<GetExecutionLogsQuery, IReadOnlyList<ExecutionLogDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public GetExecutionLogsQueryHandler(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ExecutionLogDto>> Handle(
        GetExecutionLogsQuery request,
        CancellationToken cancellationToken)
    {
        var logs = await _unitOfWork.QueryExecutionLogs.GetAllAsync(cancellationToken,
            "DynamicQuery", "User");

        var filtered = logs.AsEnumerable();

        if (request.QueryId.HasValue)
            filtered = filtered.Where(l => l.DynamicQueryId == request.QueryId.Value);

        if (request.UserId.HasValue)
            filtered = filtered.Where(l => l.UserId == request.UserId.Value);

        return _mapper.Map<IReadOnlyList<ExecutionLogDto>>(
            filtered.OrderByDescending(l => l.ExecutedAt).ToList());
    }
}
