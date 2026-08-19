using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Constants;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;

namespace Bayan.Application.Common.Security;

/// <summary>
/// The principals a viewer grant can be addressed to: the person themselves, any of their
/// roles, and any user group they belong to.
///
/// <para>
/// Resolved once per request and reused across every task in a list — the alternative is two
/// extra round trips per task. Roles and group membership are read from the database rather
/// than taken from the token, so a revoked role or group stops granting access immediately
/// instead of when the token expires an hour later.
/// </para>
/// </summary>
public sealed record ScheduledTaskPrincipal(Guid UserId, HashSet<Guid> RoleIds, HashSet<Guid> UserGroupIds)
{
    public static async Task<ScheduledTaskPrincipal> ResolveAsync(
        IUnitOfWork unitOfWork,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var userRoles = await unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == userId, cancellationToken);
        var groupIds = await UserGroupMembership.GroupIdsAsync(unitOfWork, userId, cancellationToken);

        return new ScheduledTaskPrincipal(userId, userRoles.Select(ur => ur.RoleId).ToHashSet(), groupIds);
    }
}

/// <summary>
/// Shared read-permission rule for scheduled tasks: holders of <c>scheduledTasks.viewAll</c>
/// (Admin and Auditor by default) see every task; everyone else needs a viewer grant —
/// given to one of their roles, to a user group they belong to, or to them by name.
///
/// <para>
/// Downloading a run's export files is a narrower right than viewing. An Auditor reads the
/// task's configuration and run history, but the exports themselves are query results — the
/// data the Auditor role is deliberately not given. So downloading needs the blanket
/// <c>scheduledTasks.download</c> permission, or a viewer grant carrying
/// <c>CanDownloadFiles</c>; an Auditor reached by such a grant may download, like anyone else.
/// </para>
///
/// <para>
/// Grants add up rather than override: whoever holds two keeps the more permissive one, so
/// someone named individually as a plain viewer still downloads if a role of theirs was given
/// the download right. Revoking a download means clearing it everywhere it was given.
/// </para>
/// </summary>
public static class ScheduledTaskAccess
{
    /// <summary>The navigations every check below needs loaded on the task.</summary>
    public static readonly string[] GrantIncludes =
    {
        "Viewers", "Viewers.User",
        "ViewerRoles", "ViewerRoles.Role",
        "ViewerUserGroups", "ViewerUserGroups.UserGroup"
    };

    /// <summary>Whether any grant on this task reaches the principal.</summary>
    public static bool HasViewerGrant(ScheduledTask task, ScheduledTaskPrincipal principal) =>
        task.Viewers.Any(v => v.UserId == principal.UserId)
        || task.ViewerRoles.Any(r => principal.RoleIds.Contains(r.RoleId))
        || task.ViewerUserGroups.Any(g => principal.UserGroupIds.Contains(g.UserGroupId));

    /// <summary>
    /// Whether any grant reaching the principal allows downloading this task's export files.
    /// </summary>
    public static bool HasDownloadGrant(ScheduledTask task, ScheduledTaskPrincipal principal) =>
        task.Viewers.Any(v => v.UserId == principal.UserId && v.CanDownloadFiles)
        || task.ViewerRoles.Any(r => r.CanDownloadFiles && principal.RoleIds.Contains(r.RoleId))
        || task.ViewerUserGroups.Any(g => g.CanDownloadFiles && principal.UserGroupIds.Contains(g.UserGroupId));

    public static async Task EnsureCanViewAsync(
        ScheduledTask task,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IPermissionService permissions,
        CancellationToken cancellationToken = default)
    {
        if (await permissions.HasAsync(currentUser.Roles, Permissions.ScheduledTasksViewAll, cancellationToken))
            return;

        var principal = await ScheduledTaskPrincipal.ResolveAsync(unitOfWork, currentUser.UserId, cancellationToken);
        if (!HasViewerGrant(task, principal))
            throw new ForbiddenAccessException("You do not have access to this scheduled task.");
    }

    /// <summary>
    /// Two ways in: the blanket <c>scheduledTasks.download</c> permission, or a viewer grant on
    /// this task that allows downloads — the per-task grant is what lets one person have the
    /// files for one task without being given every task's.
    /// </summary>
    public static async Task<bool> CanDownloadFilesAsync(
        ScheduledTask task,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IPermissionService permissions,
        CancellationToken cancellationToken = default)
    {
        if (await permissions.HasAsync(currentUser.Roles, Permissions.ScheduledTasksDownload, cancellationToken))
            return true;

        var principal = await ScheduledTaskPrincipal.ResolveAsync(unitOfWork, currentUser.UserId, cancellationToken);
        return HasDownloadGrant(task, principal);
    }

    public static async Task EnsureCanDownloadFilesAsync(
        ScheduledTask task,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IPermissionService permissions,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanViewAsync(task, unitOfWork, currentUser, permissions, cancellationToken);

        if (!await CanDownloadFilesAsync(task, unitOfWork, currentUser, permissions, cancellationToken))
            throw new ForbiddenAccessException("You do not have permission to download this task's files.");
    }
}
