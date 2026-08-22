using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Constants;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;

namespace Bayan.Application.Common.Security;

/// <summary>
/// Everything a report's access depends on, resolved once. Resolving it as a unit matters
/// because the "My Queries" page asks about many reports at a time, and asking per report would
/// be several round trips each.
/// </summary>
/// <param name="ReportIds">Reports granted directly — by role, by user group, or by name.</param>
/// <param name="QueryGroupIds">
/// Query groups the caller can reach. A grant on a group reaches everything inside it, reports
/// included, exactly as it already reaches that group's queries.
/// </param>
/// <param name="IsAdmin">Admin reaches everything without consulting a grant.</param>
public sealed record ReportReach(
    HashSet<Guid> ReportIds,
    HashSet<Guid> QueryGroupIds,
    bool IsAdmin);

/// <summary>
/// Who may run a report. Access is granted the same four ways query access is — by role, by user
/// group, by name, or through the query group the report sits in — and an Admin always passes.
///
/// <para>
/// The group route is the important one: to the person running it, a report is just another
/// thing on their list, filed in the same folder as the queries beside it. Putting reports in a
/// separate access world would mean granting the same team the same folder twice.
/// </para>
///
/// <para>
/// This is only the <b>outer</b> gate, and it is deliberately not the whole check. Running a
/// report fans out to a saved query per dataset, and each of those goes through
/// <c>ExecuteQueryCommand</c>, which applies its own access check. So a caller granted the
/// report but not one of the queries behind it is refused by that inner check, and a report can
/// never become a way to reach data the caller could not reach directly. The alternative —
/// running datasets as the report's author — would be straightforward privilege escalation.
/// </para>
///
/// <para>
/// A role only grants when it may run reports at all, resolved through the permission matrix
/// rather than a role name, for the same reason <see cref="QueryAccessRoles"/> does it: an
/// installation can invent roles, and assigning a report to one that cannot run reports should
/// visibly grant nothing rather than quietly appear to work.
/// </para>
/// </summary>
public static class ReportAccess
{
    /// <summary>
    /// The grant navigations, as separate include paths — the repository splats these into one
    /// <c>Include</c> call each, so a single comma-joined string would be one invalid path.
    /// </summary>
    public static readonly string[] GrantIncludes =
        { "ReportRoles", "ReportUserGroups", "ReportUsers" };

    /// <summary>
    /// The current user's role ids, minus any role that may not run reports.
    /// </summary>
    public static async Task<HashSet<Guid>> GrantingRoleIdsAsync(
        IUnitOfWork unitOfWork,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var userRoles = await unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == userId, cancellationToken, "Role.RolePermissions");

        return userRoles
            .Where(ur => ur.Role is not null && Grants(ur.Role))
            .Select(ur => ur.RoleId)
            .ToHashSet();
    }

    private static bool Grants(Role role) =>
        Permissions.IsPinned(role.Name)
        || role.RolePermissions.Any(p => p.Permission == Permissions.ReportsRun);

    /// <summary>
    /// Resolves every grant that reaches this caller, in one pass, for reuse across many reports.
    /// </summary>
    public static async Task<ReportReach> ResolveAsync(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        var isAdmin = currentUser.Roles.Contains(RoleNames.Admin);
        var reportIds = new HashSet<Guid>();
        var groupIds = new HashSet<Guid>();

        var roleIds = await GrantingRoleIdsAsync(unitOfWork, currentUser.UserId, cancellationToken);
        var userGroupIds = await UserGroupMembership.GroupIdsAsync(
            unitOfWork, currentUser.UserId, cancellationToken);

        // --- direct grants on the report itself
        if (roleIds.Count > 0)
        {
            foreach (var grant in await unitOfWork.ReportRoles.FindAsync(
                r => roleIds.Contains(r.RoleId), cancellationToken))
                reportIds.Add(grant.ReportId);
        }

        if (userGroupIds.Count > 0)
        {
            foreach (var grant in await unitOfWork.ReportUserGroups.FindAsync(
                g => userGroupIds.Contains(g.UserGroupId), cancellationToken))
                reportIds.Add(grant.ReportId);
        }

        foreach (var grant in await unitOfWork.ReportUsers.FindAsync(
            u => u.UserId == currentUser.UserId, cancellationToken))
            reportIds.Add(grant.ReportId);

        // --- grants on the query group the report is filed in
        if (roleIds.Count > 0)
        {
            foreach (var grant in await unitOfWork.QueryGroupRoles.FindAsync(
                gr => roleIds.Contains(gr.RoleId), cancellationToken))
                groupIds.Add(grant.QueryGroupId);
        }

        if (userGroupIds.Count > 0)
        {
            foreach (var grant in await unitOfWork.QueryGroupUserGroups.FindAsync(
                gg => userGroupIds.Contains(gg.UserGroupId), cancellationToken))
                groupIds.Add(grant.QueryGroupId);
        }

        foreach (var grant in await unitOfWork.QueryGroupUsers.FindAsync(
            gu => gu.UserId == currentUser.UserId, cancellationToken))
            groupIds.Add(grant.QueryGroupId);

        return new ReportReach(reportIds, groupIds, isAdmin);
    }

    /// <summary>Whether this reach covers this report. Grants are additive — any one is enough.</summary>
    public static bool Reaches(ReportReach reach, Report report) =>
        reach.IsAdmin
        || reach.ReportIds.Contains(report.Id)
        || (report.QueryGroupId.HasValue && reach.QueryGroupIds.Contains(report.QueryGroupId.Value));

    /// <summary>
    /// Whether the caller may run this report. Takes the loaded entity rather than an id because
    /// the answer depends on the group it is filed in.
    /// </summary>
    public static async Task<bool> CanRunAsync(
        Report report,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        if (currentUser.Roles.Contains(RoleNames.Admin))
            return true;

        var reach = await ResolveAsync(unitOfWork, currentUser, cancellationToken);
        return Reaches(reach, report);
    }

    /// <summary>
    /// <see cref="CanRunAsync"/>, throwing the 403 the middleware maps rather than returning false.
    /// </summary>
    public static async Task EnsureCanRunAsync(
        Report report,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        if (!await CanRunAsync(report, unitOfWork, currentUser, cancellationToken))
            throw new ForbiddenAccessException("You do not have access to this report.");
    }
}
