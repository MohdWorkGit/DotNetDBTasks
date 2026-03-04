using AutoMapper;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves parameter change history for a specific query or all queries.
/// </summary>
public class GetParameterChangeHistoryQuery : IRequest<IReadOnlyList<ParameterChangeHistoryDto>>
{
    public Guid? QueryId { get; set; }
}

public class GetParameterChangeHistoryQueryHandler
    : IRequestHandler<GetParameterChangeHistoryQuery, IReadOnlyList<ParameterChangeHistoryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public GetParameterChangeHistoryQueryHandler(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ParameterChangeHistoryDto>> Handle(
        GetParameterChangeHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var histories = request.QueryId.HasValue
            ? await _unitOfWork.ParameterChangeHistories.FindAsync(
                h => h.DynamicQueryId == request.QueryId.Value,
                cancellationToken, "DynamicQuery")
            : await _unitOfWork.ParameterChangeHistories.GetAllAsync(
                cancellationToken, "DynamicQuery");

        return _mapper.Map<IReadOnlyList<ParameterChangeHistoryDto>>(
            histories.OrderByDescending(h => h.ChangedAt).ToList());
    }
}
