using AutoMapper;
using Jadwal.Domain.Exceptions;
using Jadwal.Domain.Interfaces;
using MediatR;

namespace Jadwal.Application.Features.QueryGroups.Queries;

public record GetQueryGroupByIdQuery(Guid Id) : IRequest<QueryGroupDto>;

public class GetQueryGroupByIdQueryHandler
    : IRequestHandler<GetQueryGroupByIdQuery, QueryGroupDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public GetQueryGroupByIdQueryHandler(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<QueryGroupDto> Handle(GetQueryGroupByIdQuery request, CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.QueryGroups.GetByIdAsync(request.Id, cancellationToken,
            "QueryGroupRoles.Role", "QueryGroupUserGroups.UserGroup", "QueryGroupUsers.User", "QueryGroupQueries");
        if (group is null)
            throw new NotFoundException(nameof(Domain.Entities.QueryGroup), request.Id);

        return _mapper.Map<QueryGroupDto>(group);
    }
}
