using Bayan.Application.Common.Interfaces;
using Bayan.Application.Features.DynamicQueries.Queries;
using Bayan.Domain.Constants;
using Bayan.Domain.Interfaces;

namespace Bayan.Application.Common.Security;

/// <summary>
/// Keeps a role on the granting side of query access only when it is allowed to run a query at
/// all.
///
/// <para>
/// Roles are rows in <c>Roles</c>, so every one of them appears in the query and group access
/// pickers — including any an administrator invents. Assigning a query to a role that cannot run
/// queries would otherwise look like it granted something and do nothing. Resolving access
/// through <see cref="GrantingRoleIdsAsync"/> rather than the raw <c>UserRoles</c> rows makes
/// such an assignment inert, whatever anybody clicks.
/// </para>
///
/// <para>
/// The test used to be the role's <em>name</em> — Auditor and AccessManager were hard-coded as
/// unable to run anything. It is now the <c>queries.run</c> permission, which those two roles
/// simply do not hold by default. An administrator who ticks that box for them is making a
/// deliberate choice, and this honours it.
/// </para>
///
/// <para>
/// This is per role, not per person: someone holding a second role keeps everything that role
/// grants, and an Admin is unaffected throughout.
/// </para>
/// </summary>
public static class QueryAccessRoles
{
    /// <summary>
    /// The current user's role ids, minus any role that may not run queries.
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

    /// <summary>
    /// True when this role may run queries — pinned for Admin, otherwise the presence of
    /// <c>queries.run</c> in its permissions.
    /// </summary>
    private static bool Grants(Domain.Entities.Role role) =>
        Permissions.IsPinned(role.Name)
        || role.RolePermissions.Any(p => p.Permission == Permissions.QueriesRun);

    /// <summary>
    /// Blanks the SQL on DTOs bound for someone who may list queries but not read them. The
    /// name, description, group and current assignments stay — those are what the accessibility
    /// pages are built from.
    /// </summary>
    public static void RedactQueryTextFor(bool mayReadSql, params DynamicQueryDto[] queries) =>
        RedactQueryTextFor(mayReadSql, (IEnumerable<DynamicQueryDto>)queries);

    /// <inheritdoc cref="RedactQueryTextFor(bool, DynamicQueryDto[])"/>
    public static void RedactQueryTextFor(bool mayReadSql, IEnumerable<DynamicQueryDto> queries)
    {
        if (mayReadSql)
            return;

        foreach (var query in queries)
            query.SqlQuery = string.Empty;
    }

    /// <summary>
    /// Whether the caller may see query text. Asked of the permission matrix rather than of a
    /// role name, so a custom role can be given the query list without the SQL — which is the
    /// distinction the Access Manager role was invented for.
    /// </summary>
    public static Task<bool> MayReadSqlAsync(
        IPermissionService permissions,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken) =>
        permissions.HasAsync(currentUser.Roles, Permissions.QueriesReadSql, cancellationToken);
}
