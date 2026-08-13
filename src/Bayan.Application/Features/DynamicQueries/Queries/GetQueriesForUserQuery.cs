using AutoMapper;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves all enabled dynamic queries accessible to the current user
/// via role assignments, user group assignments, or direct user assignments.
/// </summary>
public record GetQueriesForUserQuery : IRequest<IReadOnlyList<DynamicQueryDto>>;

public class GetQueriesForUserQueryHandler
    : IRequestHandler<GetQueriesForUserQuery, IReadOnlyList<DynamicQueryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;

    public GetQueriesForUserQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<DynamicQueryDto>> Handle(
        GetQueriesForUserQuery request,
        CancellationToken cancellationToken)
    {
        var roleIds = await QueryAccessRoles.GrantingRoleIdsAsync(
            _unitOfWork, _currentUser.UserId, cancellationToken);
        var userGroupIds = await UserGroupMembership.GroupIdsAsync(
            _unitOfWork, _currentUser.UserId, cancellationToken);

        // Direct (query-level) access — role, user group, or per-user assignment on the query itself.
        var queryIds = new HashSet<Guid>();

        var queryRoles = await _unitOfWork.DynamicQueryRoles.FindAsync(
            qr => roleIds.Contains(qr.RoleId), cancellationToken);
        foreach (var qr in queryRoles)
            queryIds.Add(qr.DynamicQueryId);

        if (userGroupIds.Count > 0)
        {
            var queryUserGroups = await _unitOfWork.DynamicQueryUserGroups.FindAsync(
                qg => userGroupIds.Contains(qg.UserGroupId), cancellationToken);
            foreach (var qg in queryUserGroups)
                queryIds.Add(qg.DynamicQueryId);
        }

        var queryUsers = await _unitOfWork.DynamicQueryUsers.FindAsync(
            qu => qu.UserId == _currentUser.UserId, cancellationToken);
        foreach (var qu in queryUsers)
            queryIds.Add(qu.DynamicQueryId);

        // Group-level access — every query inside the group becomes visible.
        var groupIds = new HashSet<Guid>();

        var groupRoles = await _unitOfWork.QueryGroupRoles.FindAsync(
            gr => roleIds.Contains(gr.RoleId), cancellationToken);
        foreach (var gr in groupRoles) groupIds.Add(gr.QueryGroupId);

        if (userGroupIds.Count > 0)
        {
            var groupUserGroups = await _unitOfWork.QueryGroupUserGroups.FindAsync(
                gg => userGroupIds.Contains(gg.UserGroupId), cancellationToken);
            foreach (var gg in groupUserGroups) groupIds.Add(gg.QueryGroupId);
        }

        var groupUsers = await _unitOfWork.QueryGroupUsers.FindAsync(
            gu => gu.UserId == _currentUser.UserId, cancellationToken);
        foreach (var gu in groupUsers) groupIds.Add(gu.QueryGroupId);

        var queries = await _unitOfWork.DynamicQueries.FindAsync(
            q => q.IsEnabled && (
                queryIds.Contains(q.Id) ||
                (q.QueryGroupId.HasValue && groupIds.Contains(q.QueryGroupId.Value))),
            cancellationToken,
            "DynamicQueryRoles.Role", "DynamicQueryUserGroups.UserGroup", "DynamicQueryUsers.User",
            "Parameters", "DatabaseUser", "QueryGroup");

        return _mapper.Map<IReadOnlyList<DynamicQueryDto>>(queries);
    }
}
