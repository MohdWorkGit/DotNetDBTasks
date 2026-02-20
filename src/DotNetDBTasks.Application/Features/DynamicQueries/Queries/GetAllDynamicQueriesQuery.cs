using AutoMapper;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves all dynamic queries. Admin-only operation.
/// </summary>
public record GetAllDynamicQueriesQuery : IRequest<IReadOnlyList<DynamicQueryDto>>;

public class GetAllDynamicQueriesQueryHandler
    : IRequestHandler<GetAllDynamicQueriesQuery, IReadOnlyList<DynamicQueryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public GetAllDynamicQueriesQueryHandler(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<DynamicQueryDto>> Handle(
        GetAllDynamicQueriesQuery request,
        CancellationToken cancellationToken)
    {
        var queries = await _unitOfWork.DynamicQueries.GetAllAsync(cancellationToken);
        return _mapper.Map<IReadOnlyList<DynamicQueryDto>>(queries);
    }
}
