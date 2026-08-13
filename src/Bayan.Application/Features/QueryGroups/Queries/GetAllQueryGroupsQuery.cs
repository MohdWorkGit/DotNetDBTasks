using AutoMapper;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.QueryGroups.Queries;

/// <summary>
/// Retrieves all query groups with their access assignments. Admin/AccessManager.
/// </summary>
public record GetAllQueryGroupsQuery : IRequest<IReadOnlyList<QueryGroupDto>>;

public class GetAllQueryGroupsQueryHandler
    : IRequestHandler<GetAllQueryGroupsQuery, IReadOnlyList<QueryGroupDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public GetAllQueryGroupsQueryHandler(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<QueryGroupDto>> Handle(
        GetAllQueryGroupsQuery request,
        CancellationToken cancellationToken)
    {
        var groups = await _unitOfWork.QueryGroups.GetAllAsync(cancellationToken,
            "QueryGroupRoles.Role", "QueryGroupUserGroups.UserGroup", "QueryGroupUsers.User", "DynamicQueries");
        return _mapper.Map<IReadOnlyList<QueryGroupDto>>(groups);
    }
}
