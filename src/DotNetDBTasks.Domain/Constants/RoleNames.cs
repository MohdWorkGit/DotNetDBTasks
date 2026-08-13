namespace DotNetDBTasks.Domain.Constants;

/// <summary>
/// The role names seeded into the <c>Roles</c> table. Roles are database rows, not an
/// enum — these constants exist so handlers and <c>[Authorize]</c> attributes agree on
/// the spelling. They are <c>const</c> on purpose: attribute arguments must be
/// compile-time constants.
/// </summary>
public static class RoleNames
{
    /// <summary>Full control over every part of the system.</summary>
    public const string Admin = "Admin";

    /// <summary>Runs the queries they have been granted; no administration.</summary>
    public const string User = "User";

    /// <summary>Reads execution logs and scheduled-task history. Cannot download task output files.</summary>
    public const string Auditor = "Auditor";

    /// <summary>
    /// Decides which roles, user groups and users may reach each query and query group.
    /// Deliberately cannot read a query's SQL, edit anything, or run anything — and the
    /// role itself grants no query access (see <c>QueryAccessRoles</c>).
    /// </summary>
    public const string AccessManager = "AccessManager";

    // Composites for [Authorize(Roles = ...)].
    public const string AdminOrAuditor = Admin + "," + Auditor;
    public const string AdminOrAccessManager = Admin + "," + AccessManager;
    public const string AdminOrAuditorOrAccessManager = Admin + "," + Auditor + "," + AccessManager;
}
