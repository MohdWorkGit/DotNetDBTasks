using AutoMapper;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves a single dynamic query by its identifier.
/// </summary>
public record GetDynamicQueryByIdQuery(Guid Id) : IRequest<DynamicQueryDto>;

public class GetDynamicQueryByIdQueryHandler
    : IRequestHandler<GetDynamicQueryByIdQuery, DynamicQueryDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public GetDynamicQueryByIdQueryHandler(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<DynamicQueryDto> Handle(
        GetDynamicQueryByIdQuery request,
        CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.Id, cancellationToken,
            "DynamicQueryRoles.Role", "Parameters");
        if (query is null)
            throw new NotFoundException(nameof(Domain.Entities.DynamicQuery), request.Id);

        return _mapper.Map<DynamicQueryDto>(query);
    }
}
