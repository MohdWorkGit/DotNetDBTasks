using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;

namespace DotNetDBTasks.Application.Common.Security;

/// <summary>
/// Keeps administrator accounts out of reach of the other roles that can manage users.
///
/// <para>
/// <c>UsersController</c> is authorized for <c>Admin,Auditor</c>, so an Auditor reaches every
/// user-management handler. Without this guard an Auditor could reset an administrator's
/// password — the response returns the new password in clear text — and sign in as that
/// administrator. Blocking only that path is not enough: granting yourself (or a new account)
/// the Admin role reaches the same place, so both the *target* of a change and the *roles*
/// being handed out have to be checked.
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

    public AdminAccountGuard(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
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
            throw new ForbiddenAccessException("Only an administrator can modify an administrator account.");
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
            throw new ForbiddenAccessException("Only an administrator can grant the Admin role.");
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
