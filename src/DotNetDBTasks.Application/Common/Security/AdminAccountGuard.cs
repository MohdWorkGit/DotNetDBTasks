using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;

namespace DotNetDBTasks.Application.Common.Security;

/// <summary>
/// Keeps administrator accounts out of reach of any non-Admin role that can manage users.
///
/// <para>
/// <c>UsersController</c> is authorized for <c>Admin,AccessManager</c>, so an Access Manager
/// reaches every user-management handler. Without this guard they could reset an
/// administrator's password — the response returns the new password in clear text — and sign
/// in as that administrator. Blocking only that path is not enough: granting yourself (or a
/// new account) the Admin role reaches the same place. So three things are checked — the
/// *target* of a change, the *roles* being handed out, and whether the caller is editing
/// *their own* role set.
/// </para>
///
/// <para>
/// The self-edit rule exists because Auditor and AccessManager are defined as roles that never
/// run queries (see <see cref="QueryAccessRoles"/>). An Access Manager who could grant
/// themselves the User role would undo that in one click: assign a query to themselves, then
/// run it. They can still mint a separate account and log in as it — that is inherent in
/// letting a role both create users and control query access — but that leaves an account and
/// an audit trail behind, where a self-grant leaves neither.
/// </para>
///
/// <para>
/// Admins are unaffected — they may still manage administrator accounts, including their own.
/// </para>
/// </summary>
public sealed class AdminAccountGuard
{
    private const string AdminRoleName = "Admin";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IAppLocalizer _messages;

    public AdminAccountGuard(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IAppLocalizer messages)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _messages = messages;
    }

    private bool CallerIsAdmin => _currentUser.Roles.Contains(AdminRoleName);

    /// <summary>
    /// Throws when a non-Admin caller tries to modify a user who holds the Admin role.
    /// Call this after the target user has been loaded, so a missing user still reports 404.
    /// </summary>
    public async Task EnsureCanModifyUserAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        if (CallerIsAdmin)
            return;

        if (await IsAdminAsync(targetUserId, cancellationToken))
            throw new ForbiddenAccessException(_messages[MessageKeys.OnlyAdminCanModifyAdmin]);
    }

    /// <summary>
    /// Throws when a non-Admin caller tries to assign the Admin role. Covers both creating a
    /// new administrator and promoting an existing account (including the caller's own).
    /// </summary>
    public async Task EnsureCanAssignRolesAsync(IEnumerable<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (CallerIsAdmin)
            return;

        var adminRoleId = await GetAdminRoleIdAsync(cancellationToken);
        if (adminRoleId is not null && roleIds.Contains(adminRoleId.Value))
            throw new ForbiddenAccessException(_messages[MessageKeys.OnlyAdminCanGrantAdmin]);
    }

    /// <summary>
    /// Throws when a non-Admin caller tries to edit their own role set. Managing other people's
    /// roles is the job; handing yourself a new one is not.
    /// </summary>
    public void EnsureNotSelfRoleChange(Guid targetUserId)
    {
        if (CallerIsAdmin)
            return;

        if (targetUserId == _currentUser.UserId)
            throw new ForbiddenAccessException(_messages[MessageKeys.CannotChangeOwnRoles]);
    }

    private async Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var adminRoleId = await GetAdminRoleIdAsync(cancellationToken);
        if (adminRoleId is null)
            return false;

        return await _unitOfWork.UserRoles.ExistsAsync(
            ur => ur.UserId == userId && ur.RoleId == adminRoleId.Value, cancellationToken);
    }

    private async Task<Guid?> GetAdminRoleIdAsync(CancellationToken cancellationToken)
    {
        var adminRole = (await _unitOfWork.Roles.FindAsync(
            r => r.Name == AdminRoleName, cancellationToken)).FirstOrDefault();
        return adminRole?.Id;
    }
}
