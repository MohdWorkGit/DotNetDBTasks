using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Constants;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;

namespace Bayan.Application.Common.Security;

/// <summary>
/// Who may run a saved query: a grant by role, by user group, by name, or on the query group it
/// is filed in — and, when the query runs through a stored database login, a role grant on that
/// login too. Admin always passes.
///
/// <para>Lives here rather than inside <c>ExecuteQueryCommand</c> because a result can be handed
/// out without an execution: a dashboard tile serves one cached result to every viewer, and each
/// of them must pass exactly the check they would have faced running the query themselves.</para>
/// </summary>
public static class QueryAccess
{
    /// <summary>Throws the 403 the middleware maps unless the caller holds a grant reaching this query.</summary>
    public static async Task EnsureCanRunAsync(
        DynamicQuery query,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        if (currentUser.Roles.Contains(RoleNames.Admin))
            return;

        var userRoleIds = await QueryAccessRoles.GrantingRoleIdsAsync(
            unitOfWork, currentUser.UserId, cancellationToken);
        var userGroupIds = await UserGroupMembership.GroupIdsAsync(
            unitOfWork, currentUser.UserId, cancellationToken);

        var queryRoles = await unitOfWork.DynamicQueryRoles.FindAsync(
            qr => qr.DynamicQueryId == query.Id, cancellationToken);
        var hasAccess = queryRoles.Any(qr => userRoleIds.Contains(qr.RoleId));

        // Check user-group access
        if (!hasAccess && userGroupIds.Count > 0)
        {
            hasAccess = await unitOfWork.DynamicQueryUserGroups.ExistsAsync(
                qg => qg.DynamicQueryId == query.Id && userGroupIds.Contains(qg.UserGroupId),
                cancellationToken);
        }

        // Check direct user assignment
        if (!hasAccess)
        {
            hasAccess = await unitOfWork.DynamicQueryUsers.ExistsAsync(
                qu => qu.DynamicQueryId == query.Id && qu.UserId == currentUser.UserId,
                cancellationToken);
        }

        if (!hasAccess)
        {
            hasAccess = await HasGroupGrantAsync(
                query.Id, userRoleIds, userGroupIds, unitOfWork, currentUser.UserId, cancellationToken);
        }

        if (!hasAccess)
            throw new ForbiddenAccessException("You do not have access to this query.");
    }

    /// <summary>
    /// True when the caller holds a grant — by role, user group or name — on any of the groups
    /// the query is filed in. A query can sit in several groups, and each one's grants reach it.
    /// </summary>
    public static async Task<bool> HasGroupGrantAsync(
        Guid queryId,
        IReadOnlyCollection<Guid> userRoleIds,
        IReadOnlyCollection<Guid> userGroupIds,
        IUnitOfWork unitOfWork,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var groupIds = (await unitOfWork.QueryGroupQueries.FindAsync(
                m => m.DynamicQueryId == queryId, cancellationToken))
            .Select(m => m.QueryGroupId)
            .ToList();
        if (groupIds.Count == 0)
            return false;

        if (userRoleIds.Count > 0 && await unitOfWork.QueryGroupRoles.ExistsAsync(
                gr => groupIds.Contains(gr.QueryGroupId) && userRoleIds.Contains(gr.RoleId),
                cancellationToken))
            return true;

        if (userGroupIds.Count > 0 && await unitOfWork.QueryGroupUserGroups.ExistsAsync(
                gg => groupIds.Contains(gg.QueryGroupId) && userGroupIds.Contains(gg.UserGroupId),
                cancellationToken))
            return true;

        return await unitOfWork.QueryGroupUsers.ExistsAsync(
            gu => groupIds.Contains(gu.QueryGroupId) && gu.UserId == userId,
            cancellationToken);
    }

    /// <summary>
    /// Loads a stored database login and confirms it is active and that one of the caller's roles
    /// may use it (Admins bypass the role check).
    /// </summary>
    public static async Task<DatabaseUser> EnsureCanUseDatabaseUserAsync(
        Guid databaseUserId,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        var dbUser = await unitOfWork.DatabaseUsers.GetByIdAsync(databaseUserId, cancellationToken);
        if (dbUser is null)
            throw new NotFoundException(nameof(DatabaseUser), databaseUserId);

        if (!dbUser.IsActive)
            throw new DomainException($"Database user '{dbUser.Name}' is currently disabled.");

        if (!currentUser.Roles.Contains(RoleNames.Admin))
        {
            var userRoleIds = await QueryAccessRoles.GrantingRoleIdsAsync(
                unitOfWork, currentUser.UserId, cancellationToken);

            var hasDbAccess = userRoleIds.Count > 0 && await unitOfWork.DatabaseUserRoleAccess.ExistsAsync(
                a => a.DatabaseUserId == databaseUserId && userRoleIds.Contains(a.RoleId),
                cancellationToken);

            if (!hasDbAccess)
                throw new ForbiddenAccessException($"You do not have access to database user '{dbUser.Name}'.");
        }

        return dbUser;
    }
}
