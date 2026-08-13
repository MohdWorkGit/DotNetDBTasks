using AutoMapper;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Application.Features.DynamicQueries.Queries;
using Bayan.Domain.Entities;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.QueryGroups.Queries;

/// <summary>
/// Builds the "My Queries" view, grouped by QueryGroup. A user sees:
///   - every query they have direct/role/user-group access to, and
///   - every query inside a group they have direct/role/user-group access to.
/// Queries with no group are bucketed into a synthetic "Ungrouped" entry (Id=null).
/// Empty groups are omitted.
/// </summary>
public record GetMyQueryGroupsQuery : IRequest<IReadOnlyList<MyQueryGroupDto>>;

public class GetMyQueryGroupsQueryHandler
    : IRequestHandler<GetMyQueryGroupsQuery, IReadOnlyList<MyQueryGroupDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;

    public GetMyQueryGroupsQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<MyQueryGroupDto>> Handle(
        GetMyQueryGroupsQuery request,
        CancellationToken cancellationToken)
    {
        // Through QueryAccessRoles, not the raw UserRoles rows: a query assigned to Auditor
        // or AccessManager grants nothing, and listing it here would put a card on the page
        // that refuses to open when clicked.
        var roleIds = await QueryAccessRoles.GrantingRoleIdsAsync(
            _unitOfWork, _currentUser.UserId, cancellationToken);
        var userGroupIds = await UserGroupMembership.GroupIdsAsync(
            _unitOfWork, _currentUser.UserId, cancellationToken);

        // 1. Resolve query-level access (role / user group / direct user).
        var accessibleQueryIds = new HashSet<Guid>();

        var queryRoles = await _unitOfWork.DynamicQueryRoles.FindAsync(
            qr => roleIds.Contains(qr.RoleId), cancellationToken);
        foreach (var qr in queryRoles) accessibleQueryIds.Add(qr.DynamicQueryId);

        if (userGroupIds.Count > 0)
        {
            var queryUserGroups = await _unitOfWork.DynamicQueryUserGroups.FindAsync(
                qg => userGroupIds.Contains(qg.UserGroupId), cancellationToken);
            foreach (var qg in queryUserGroups) accessibleQueryIds.Add(qg.DynamicQueryId);
        }

        var queryUsers = await _unitOfWork.DynamicQueryUsers.FindAsync(
            qu => qu.UserId == _currentUser.UserId, cancellationToken);
        foreach (var qu in queryUsers) accessibleQueryIds.Add(qu.DynamicQueryId);

        // 2. Resolve group-level access — every query inside an accessible group is visible.
        var accessibleGroupIds = new HashSet<Guid>();

        var groupRoles = await _unitOfWork.QueryGroupRoles.FindAsync(
            gr => roleIds.Contains(gr.RoleId), cancellationToken);
        foreach (var gr in groupRoles) accessibleGroupIds.Add(gr.QueryGroupId);

        if (userGroupIds.Count > 0)
        {
            var groupUserGroups = await _unitOfWork.QueryGroupUserGroups.FindAsync(
                gg => userGroupIds.Contains(gg.UserGroupId), cancellationToken);
            foreach (var gg in groupUserGroups) accessibleGroupIds.Add(gg.QueryGroupId);
        }

        var groupUsers = await _unitOfWork.QueryGroupUsers.FindAsync(
            gu => gu.UserId == _currentUser.UserId, cancellationToken);
        foreach (var gu in groupUsers) accessibleGroupIds.Add(gu.QueryGroupId);

        // 3. Pull the queries — those directly accessible OR inside an accessible group.
        var queries = await _unitOfWork.DynamicQueries.FindAsync(
            q => q.IsEnabled && (
                accessibleQueryIds.Contains(q.Id) ||
                (q.QueryGroupId.HasValue && accessibleGroupIds.Contains(q.QueryGroupId.Value))),
            cancellationToken,
            "DynamicQueryRoles.Role", "DynamicQueryUserGroups.UserGroup", "DynamicQueryUsers.User",
            "Parameters", "DatabaseUser", "QueryGroup");

        // 4. Bucket queries by group, ordering groups by name and "Ungrouped" last.
        var groupedByGroup = queries
            .GroupBy(q => q.QueryGroupId)
            .Select(g =>
            {
                var first = g.First();
                return new MyQueryGroupDto
                {
                    Id = g.Key,
                    Name = g.Key.HasValue ? first.QueryGroup?.Name ?? "Unknown" : "Ungrouped",
                    Description = g.Key.HasValue ? first.QueryGroup?.Description ?? string.Empty : string.Empty,
                    Queries = _mapper.Map<List<DynamicQueryDto>>(g.OrderBy(q => q.Name).ToList())
                };
            })
            .OrderBy(g => g.Id.HasValue ? 0 : 1)
            .ThenBy(g => g.Name)
            .ToList();

        return groupedByGroup;
    }
}
