using AutoMapper;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Application.Features.DynamicQueries.Queries;
using Bayan.Application.Features.Reports;
using Bayan.Application.Features.Reports.Dtos;
using Bayan.Domain.Entities;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.QueryGroups.Queries;

/// <summary>
/// Builds the "My Queries" view, grouped by QueryGroup. A user sees:
///   - every query they have direct/role/user-group access to, and
///   - every query inside a group they have direct/role/user-group access to,
///   - and, in those same groups, every <b>report</b> reachable the same ways.
/// Items with no group are bucketed into a synthetic "Ungrouped" entry (Id=null).
/// Empty groups are omitted.
///
/// <para>Reports are listed here rather than on a page of their own because the distinction is
/// an authoring one. To the person running it, a report is just another thing on their list,
/// filed in the same folder as the queries beside it — and a separate page would mean granting
/// the same team the same folder twice.</para>
///
/// <para>The two are gated independently, though: query access needs <c>queries.run</c> and
/// report access needs <c>reports.run</c>, so a role holding only one sees only that kind.</para>
/// </summary>
public record GetMyQueryGroupsQuery : IRequest<IReadOnlyList<MyQueryGroupDto>>;

public class GetMyQueryGroupsQueryHandler
    : IRequestHandler<GetMyQueryGroupsQuery, IReadOnlyList<MyQueryGroupDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;

    public GetMyQueryGroupsQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser,
        IPermissionService permissions)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
        _permissions = permissions;
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

        // 4. The reports this caller may run, resolved through their own permission and their
        //    own grants — plus the group grants already computed above, since a grant on a
        //    folder reaches everything filed in it.
        var held = await _permissions.GetForRolesAsync(_currentUser.Roles, cancellationToken);
        var reportReach = await ReportAccess.ResolveAsync(_unitOfWork, _currentUser, cancellationToken);

        var reports = await _unitOfWork.Reports.FindAsync(
            r => r.IsEnabled, cancellationToken, "Datasets", "Parameters", "QueryGroup");
        var visibleReports = reports.Where(r => ReportAccess.Reaches(reportReach, r)).ToList();

        // 5. Bucket both kinds by group, ordering groups by name and "Ungrouped" last. A group
        //    appears when it holds anything the caller can reach, of either kind.
        var groupIds = queries.Select(q => q.QueryGroupId)
            .Concat(visibleReports.Select(r => r.QueryGroupId))
            .Distinct()
            .ToList();

        var groupedByGroup = groupIds
            .Select(groupId =>
            {
                var groupQueries = queries.Where(q => q.QueryGroupId == groupId).OrderBy(q => q.Name).ToList();
                var groupReports = visibleReports.Where(r => r.QueryGroupId == groupId).OrderBy(r => r.Name).ToList();

                var group = groupQueries.FirstOrDefault()?.QueryGroup
                            ?? groupReports.FirstOrDefault()?.QueryGroup;

                return new MyQueryGroupDto
                {
                    Id = groupId,
                    Name = groupId.HasValue ? group?.Name ?? "Unknown" : "Ungrouped",
                    Description = groupId.HasValue ? group?.Description ?? string.Empty : string.Empty,
                    Queries = _mapper.Map<List<DynamicQueryDto>>(groupQueries),
                    Reports = groupReports.Select(r => ReportMapper.ToSummary(r, held)).ToList()
                };
            })
            .OrderBy(g => g.Id.HasValue ? 0 : 1)
            .ThenBy(g => g.Name)
            .ToList();

        return groupedByGroup;
    }
}
