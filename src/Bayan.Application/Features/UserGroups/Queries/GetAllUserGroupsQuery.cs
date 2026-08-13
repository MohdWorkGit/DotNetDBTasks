using AutoMapper;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.UserGroups.Queries;

/// <summary>
/// Retrieves all user groups with their members. Admin/AccessManager.
/// </summary>
public record GetAllUserGroupsQuery : IRequest<IReadOnlyList<UserGroupDto>>;

public class GetAllUserGroupsQueryHandler
    : IRequestHandler<GetAllUserGroupsQuery, IReadOnlyList<UserGroupDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public GetAllUserGroupsQueryHandler(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<UserGroupDto>> Handle(
        GetAllUserGroupsQuery request,
        CancellationToken cancellationToken)
    {
        var groups = await _unitOfWork.UserGroups.GetAllAsync(cancellationToken, "Members.User");
        return _mapper.Map<IReadOnlyList<UserGroupDto>>(groups.OrderBy(g => g.Name).ToList());
    }
}
