using AutoMapper;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves a single enabled dynamic query by its identifier,
/// verifying the current user has access via role, user group, or direct assignment.
/// </summary>
public record GetQueryForUserByIdQuery(Guid Id) : IRequest<DynamicQueryDto>;

public class GetQueryForUserByIdQueryHandler
    : IRequestHandler<GetQueryForUserByIdQuery, DynamicQueryDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;

    public GetQueryForUserByIdQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<DynamicQueryDto> Handle(
        GetQueryForUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.Id, cancellationToken,
            "DynamicQueryRoles.Role", "DynamicQueryUserGroups.UserGroup", "DynamicQueryUsers.User",
            "Parameters", "DatabaseUser",
            "QueryGroup.QueryGroupRoles", "QueryGroup.QueryGroupUserGroups", "QueryGroup.QueryGroupUsers");

        if (query is null || !query.IsEnabled)
            throw new NotFoundException(nameof(Domain.Entities.DynamicQuery), request.Id);

        var roleIds = await QueryAccessRoles.GrantingRoleIdsAsync(
            _unitOfWork, _currentUser.UserId, cancellationToken);
        var userGroupIds = await UserGroupMembership.GroupIdsAsync(
            _unitOfWork, _currentUser.UserId, cancellationToken);

        var hasRoleAccess = query.DynamicQueryRoles.Any(qr => roleIds.Contains(qr.RoleId));
        var hasUserGroupAccess = query.DynamicQueryUserGroups.Any(qg => userGroupIds.Contains(qg.UserGroupId));
        var hasDirectAccess = query.DynamicQueryUsers.Any(qu => qu.UserId == _currentUser.UserId);

        // Group-level access: any assignment on the parent group grants access to this query.
        var hasGroupAccess = query.QueryGroup is not null && (
            query.QueryGroup.QueryGroupRoles.Any(gr => roleIds.Contains(gr.RoleId)) ||
            query.QueryGroup.QueryGroupUserGroups.Any(gg => userGroupIds.Contains(gg.UserGroupId)) ||
            query.QueryGroup.QueryGroupUsers.Any(gu => gu.UserId == _currentUser.UserId));

        if (!hasRoleAccess && !hasUserGroupAccess && !hasDirectAccess && !hasGroupAccess)
            throw new ForbiddenAccessException("You do not have access to this query.");

        return _mapper.Map<DynamicQueryDto>(query);
    }
}
