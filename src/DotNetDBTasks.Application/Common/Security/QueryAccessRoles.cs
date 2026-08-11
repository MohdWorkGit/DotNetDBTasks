using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Domain.Constants;
using DotNetDBTasks.Domain.Interfaces;

namespace DotNetDBTasks.Application.Common.Security;

/// <summary>
/// Keeps the oversight roles on the granting side of query access rather than the
/// receiving side.
///
/// <para>
/// Auditor and AccessManager are defined as roles that never run queries. Both are ordinary
/// rows in <c>Roles</c>, so both appear in the query and group access pickers, and assigning
/// a query to either — by accident or on purpose — would otherwise hand every holder of that
/// role the ability to open and run it. Resolving access through
/// <see cref="GrantingRoleIdsAsync"/> instead of the raw <c>UserRoles</c> rows makes such an
/// assignment inert, so the guarantee holds no matter what an administrator clicks.
/// </para>
///
/// <para>
/// This is per role, not per person. Someone holding a second role keeps everything that role
/// grants: an Auditor who is also a User runs whatever User is assigned, and an Admin is
/// unaffected throughout.
/// </para>
/// </summary>
public static class QueryAccessRoles
{
    /// <summary>Roles that never confer access to a query, however it is assigned.</summary>
    private static readonly string[] NonGrantingRoles = { RoleNames.Auditor, RoleNames.AccessManager };

    /// <summary>
    /// The current user's role ids, minus any role that does not confer query access.
    /// </summary>
    public static async Task<HashSet<Guid>> GrantingRoleIdsAsync(
        IUnitOfWork unitOfWork,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var userRoles = await unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == userId, cancellationToken, "Role");

        return userRoles
            .Where(ur => ur.Role is null || !NonGrantingRoles.Contains(ur.Role.Name))
            .Select(ur => ur.RoleId)
            .ToHashSet();
    }

    /// <summary>True when this role name never confers query access. Used by the API's role list.</summary>
    public static bool GrantsQueryAccess(string roleName) => !NonGrantingRoles.Contains(roleName);

    /// <summary>
    /// True when the caller reaches an admin query endpoint as an Access Manager and nothing
    /// more. Such a caller manages accessibility and must not read the query text itself.
    /// </summary>
    public static bool HidesQueryText(ICurrentUserService currentUser) =>
        currentUser.Roles.Contains(RoleNames.AccessManager)
        && !currentUser.Roles.Contains(RoleNames.Admin);

    /// <summary>
    /// Blanks the SQL on DTOs bound for an Access Manager. The name, description, group and
    /// current assignments stay — those are what the accessibility pages are built from.
    /// </summary>
    public static void RedactQueryTextFor(ICurrentUserService currentUser, params DynamicQueryDto[] queries)
    {
        if (!HidesQueryText(currentUser))
            return;

        foreach (var query in queries)
            query.SqlQuery = string.Empty;
    }

    /// <inheritdoc cref="RedactQueryTextFor(ICurrentUserService, DynamicQueryDto[])"/>
    public static void RedactQueryTextFor(ICurrentUserService currentUser, IEnumerable<DynamicQueryDto> queries)
    {
        if (!HidesQueryText(currentUser))
            return;

        foreach (var query in queries)
            query.SqlQuery = string.Empty;
    }
}
