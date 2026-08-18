namespace Bayan.Domain.Constants;

/// <summary>
/// Every capability the system can grant, and the default each seeded role holds.
///
/// <para>
/// These replaced <c>[Authorize(Roles = ...)]</c> as the thing endpoints check. A role is now
/// just a named bag of these, editable at runtime on <b>Settings → Permissions</b>, which is
/// what makes a custom role possible at all: the code cannot know the names an installation
/// will invent, but it does know the capabilities it offers.
/// </para>
///
/// <para>
/// The strings are persisted in <c>RolePermissions</c>, so renaming one silently revokes it —
/// treat them as a contract, exactly like the setting keys. Adding one is safe: no role holds
/// it until someone ticks the box, so a new capability starts closed.
/// </para>
/// </summary>
public static class Permissions
{
    // ---- Queries
    /// <summary>See the admin query list and open a query's definition (without its SQL).</summary>
    public const string QueriesView = "queries.view";

    /// <summary>See the SQL text itself, on the list, the editor and the export.</summary>
    public const string QueriesReadSql = "queries.readSql";

    /// <summary>Create, edit and delete queries, their parameters and their Word templates.</summary>
    public const string QueriesManage = "queries.manage";

    /// <summary>Export query definitions to JSON and import them back.</summary>
    public const string QueriesTransfer = "queries.transfer";

    /// <summary>Run a query that has been granted to you, and see My Queries and History.</summary>
    public const string QueriesRun = "queries.run";

    // ---- Export formats
    //
    // Export is gated twice, and a download needs both to agree: the query lists the formats it
    // may be exported as at all, and the role says which of those this person may use. Either
    // one alone is not enough — a query that permits PDF gives nothing to a role without
    // queries.exportPdf, and vice versa. Neither is seeded, so export starts closed everywhere
    // and is opened deliberately.

    /// <summary>Download a result as Excel, where the query permits it.</summary>
    public const string QueriesExportExcel = "queries.exportExcel";

    /// <summary>Download a result as CSV, where the query permits it.</summary>
    public const string QueriesExportCsv = "queries.exportCsv";

    /// <summary>Download a result as JSON, where the query permits it.</summary>
    public const string QueriesExportJson = "queries.exportJson";

    /// <summary>Download a result as PDF, where the query permits it.</summary>
    public const string QueriesExportPdf = "queries.exportPdf";

    /// <summary>Download a result as Word, where the query permits it.</summary>
    public const string QueriesExportWord = "queries.exportWord";

    // ---- Access assignment
    /// <summary>Decide who may reach an individual query.</summary>
    public const string AccessManageQuery = "access.manageQuery";

    /// <summary>Decide who may reach a query group, and so everything inside it.</summary>
    public const string AccessManageGroup = "access.manageGroup";

    // ---- Query groups
    /// <summary>Create, rename and delete query groups.</summary>
    public const string QueryGroupsManage = "queryGroups.manage";

    // ---- User groups
    /// <summary>See the user groups and who is in them.</summary>
    public const string UserGroupsView = "userGroups.view";

    /// <summary>Create, rename and delete user groups, and set their membership.</summary>
    public const string UserGroupsManage = "userGroups.manage";

    // ---- Users
    /// <summary>See the user list.</summary>
    public const string UsersView = "users.view";

    /// <summary>Create accounts, reset passwords, change roles, activate and deactivate.</summary>
    public const string UsersManage = "users.manage";

    // ---- Directory
    /// <summary>Read directory data — the department list and the imported accounts.</summary>
    public const string DirectoryView = "directory.view";

    /// <summary>Search the directory, import accounts from it, revoke, restore and sync.</summary>
    public const string DirectoryManage = "directory.manage";

    // ---- Database connections
    /// <summary>Create and edit the database connections queries run through.</summary>
    public const string DatabaseUsersManage = "databaseUsers.manage";

    // ---- Scheduled tasks
    /// <summary>See every scheduled task and its run history, not only your own.</summary>
    public const string ScheduledTasksViewAll = "scheduledTasks.viewAll";

    /// <summary>Create, edit, delete, run and cancel scheduled tasks.</summary>
    public const string ScheduledTasksManage = "scheduledTasks.manage";

    /// <summary>Download the files a scheduled run produced.</summary>
    public const string ScheduledTasksDownload = "scheduledTasks.download";

    // ---- Oversight
    /// <summary>Read the query execution log and the before-change snapshots.</summary>
    public const string LogsView = "logs.view";

    /// <summary>Read the system audit trail of administrative actions.</summary>
    public const string AuditView = "audit.view";

    // ---- System
    /// <summary>Change the site logo.</summary>
    public const string BrandingManage = "branding.manage";

    /// <summary>Read and change the runtime settings.</summary>
    public const string SettingsManage = "settings.manage";

    /// <summary>Create and delete roles, and change what any role may do.</summary>
    public const string RolesManage = "roles.manage";

    /// <summary>Every permission, in the order the Permissions tab lists them.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        QueriesView, QueriesReadSql, QueriesManage, QueriesTransfer, QueriesRun,
        QueriesExportExcel, QueriesExportCsv, QueriesExportJson, QueriesExportPdf, QueriesExportWord,
        AccessManageQuery, AccessManageGroup, QueryGroupsManage,
        UserGroupsView, UserGroupsManage,
        UsersView, UsersManage,
        DirectoryView, DirectoryManage,
        DatabaseUsersManage,
        ScheduledTasksViewAll, ScheduledTasksManage, ScheduledTasksDownload,
        LogsView, AuditView,
        BrandingManage, SettingsManage, RolesManage
    };

    /// <summary>
    /// What each seeded role starts with — the behaviour these roles had when permissions were
    /// still compiled into <c>[Authorize]</c> attributes, so an upgrade changes nothing until
    /// an administrator edits the matrix.
    ///
    /// <para>Admin is absent on purpose: it always holds everything (see
    /// <see cref="IsPinned"/>), and storing that would only invite it to drift.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> SeededDefaults =
        new Dictionary<string, string[]>
        {
            [RoleNames.User] = new[] { QueriesRun },

            [RoleNames.Auditor] = new[] { LogsView, AuditView, ScheduledTasksViewAll },

            // Mirrors what this role could do before, including the two runtime toggles it used
            // to depend on: group access on, per-query access off, user groups on.
            // Note what is absent: QueriesReadSql, QueriesManage and QueryGroupsManage. This
            // role has always decided who may reach a query without being able to read or
            // change one.
            [RoleNames.AccessManager] = new[]
            {
                QueriesView, AccessManageGroup,
                UserGroupsView, UserGroupsManage,
                UsersView, UsersManage,
                DirectoryView
            }
        };

    /// <summary>
    /// True for the role that cannot be edited or deleted. Admin holds every permission by
    /// definition: the alternative is an installation where the last account that could open
    /// the Permissions tab has just been locked out of it.
    /// </summary>
    public static bool IsPinned(string roleName) =>
        string.Equals(roleName, RoleNames.Admin, StringComparison.OrdinalIgnoreCase);
}
