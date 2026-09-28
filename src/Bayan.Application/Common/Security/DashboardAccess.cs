using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Constants;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;

namespace Bayan.Application.Common.Security;

/// <summary>
/// Who may open a dashboard: a grant by role, by user group or by name. Admin always passes.
///
/// <para>This is only the <b>outer</b> gate, exactly as <see cref="ReportAccess"/> is for reports.
/// Every tile is also checked against its own query with <see cref="QueryAccess"/> — including when
/// its data comes from the shared cache — so a viewer granted the dashboard but not one of the
/// queries behind it sees that tile refused, never its data.</para>
///
/// <para>A role only grants when it may open dashboards at all, resolved through the permission
/// matrix, for the same reason <see cref="ReportAccess"/> does it.</para>
/// </summary>
public static class DashboardAccess
{
    /// <summary>The dashboard ids granted to this caller, or null for an Admin, who reaches all of them.</summary>
    public static async Task<HashSet<Guid>?> ReachableIdsAsync(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        if (currentUser.Roles.Contains(RoleNames.Admin))
            return null;

        var ids = new HashSet<Guid>();

        var userRoles = await unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == currentUser.UserId, cancellationToken, "Role.RolePermissions");
        var roleIds = userRoles
            .Where(ur => ur.Role is not null && Grants(ur.Role))
            .Select(ur => ur.RoleId)
            .ToHashSet();

        if (roleIds.Count > 0)
        {
            foreach (var grant in await unitOfWork.DashboardRoles.FindAsync(
                r => roleIds.Contains(r.RoleId), cancellationToken))
                ids.Add(grant.DashboardId);
        }

        var userGroupIds = await UserGroupMembership.GroupIdsAsync(
            unitOfWork, currentUser.UserId, cancellationToken);
        if (userGroupIds.Count > 0)
        {
            foreach (var grant in await unitOfWork.DashboardUserGroups.FindAsync(
                g => userGroupIds.Contains(g.UserGroupId), cancellationToken))
                ids.Add(grant.DashboardId);
        }

        foreach (var grant in await unitOfWork.DashboardUsers.FindAsync(
            u => u.UserId == currentUser.UserId, cancellationToken))
            ids.Add(grant.DashboardId);

        return ids;
    }

    private static bool Grants(Role role) =>
        Permissions.IsPinned(role.Name)
        || role.RolePermissions.Any(p => p.Permission == Permissions.DashboardsRun);

    /// <summary>Throws the 403 the middleware maps unless the caller may open this dashboard.</summary>
    public static async Task EnsureCanViewAsync(
        Guid dashboardId,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        var reachable = await ReachableIdsAsync(unitOfWork, currentUser, cancellationToken);
        if (reachable is not null && !reachable.Contains(dashboardId))
            throw new ForbiddenAccessException("You do not have access to this dashboard.");
    }
}
