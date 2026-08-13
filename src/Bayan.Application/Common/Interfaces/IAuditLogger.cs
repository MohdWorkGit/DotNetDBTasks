namespace Bayan.Application.Common.Interfaces;

/// <summary>One administrative action, ready to be written to the audit trail.</summary>
public sealed class AuditEntry
{
    /// <summary>Stable code such as <c>users.create</c>. See <see cref="AuditActions"/>.</summary>
    public string Action { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? EntityName { get; set; }
    public string? DetailsJson { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Records administrative actions.
///
/// <para>
/// Most callers never touch this: the MediatR audit behavior covers every command
/// automatically. Use it directly only for mutations that do not go through MediatR — the
/// LDAP import endpoints and the branding upload, which act on the controller.
/// </para>
///
/// <para>
/// Implementations must never throw. An audit write failing is worth a log line, but it must
/// not turn a successful administrative action into an error the user sees.
/// </para>
/// </summary>
public interface IAuditLogger
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// The action codes written to <c>SystemAuditLog.Action</c>, and the mapping from MediatR
/// command type names onto them.
///
/// <para>
/// Codes are machine-stable and translated in the UI, so renaming one silently changes how
/// history reads — treat these as a persisted contract, not display strings.
/// </para>
/// </summary>
public static class AuditActions
{
    public const string CategoryUsers = "users";
    public const string CategoryQueries = "queries";
    public const string CategoryGroups = "groups";

    /// <summary>App-owned user groups — membership here is what a group grant resolves to.</summary>
    public const string CategoryUserGroups = "userGroups";

    /// <summary>Roles and what they may do — the Permissions tab.</summary>
    public const string CategoryRoles = "roles";
    public const string CategoryAccess = "access";
    public const string CategoryDatabaseUsers = "databaseUsers";
    public const string CategoryScheduledTasks = "scheduledTasks";
    public const string CategoryDirectory = "directory";
    public const string CategoryBranding = "branding";
    public const string CategorySettings = "settings";
    public const string CategoryOther = "other";

    // Directory and branding are raised from controllers, which is why they are named here
    // rather than derived from a command type.
    public const string DirectoryImportUsers = "directory.importUsers";
    public const string DirectoryImportDepartment = "directory.importDepartment";
    public const string DirectoryRevoke = "directory.revoke";
    public const string DirectoryRestore = "directory.restore";
    public const string DirectorySync = "directory.sync";
    public const string BrandingLogoSet = "branding.logoSet";
    public const string BrandingLogoRemoved = "branding.logoRemoved";
    public const string QueriesImport = "queries.import";
    public const string SettingsUpdated = "settings.updated";
    public const string RolesCreate = "roles.create";
    public const string RolesUpdate = "roles.update";
    public const string RolesDelete = "roles.delete";
    public const string RolesSetPermissions = "roles.setPermissions";

    /// <summary>
    /// Command type name -> (action code, category).
    ///
    /// <para>An unmapped command still gets logged — see <see cref="Resolve"/> — so forgetting
    /// to add an entry here degrades the label, not the coverage.</para>
    /// </summary>
    private static readonly Dictionary<string, (string Action, string Category)> Map = new()
    {
        ["CreateUserCommand"] = ("users.create", CategoryUsers),
        ["ChangeUsernameCommand"] = ("users.changeUsername", CategoryUsers),
        ["ChangePasswordCommand"] = ("users.changePassword", CategoryUsers),
        ["ResetPasswordCommand"] = ("users.resetPassword", CategoryUsers),
        ["ChangeUserRoleCommand"] = ("users.changeRoles", CategoryUsers),
        ["ToggleUserActiveCommand"] = ("users.toggleActive", CategoryUsers),

        ["CreateDynamicQueryCommand"] = ("queries.create", CategoryQueries),
        ["UpdateDynamicQueryCommand"] = ("queries.update", CategoryQueries),
        ["DeleteDynamicQueryCommand"] = ("queries.delete", CategoryQueries),
        ["SetQueryWordTemplateCommand"] = ("queries.setTemplate", CategoryQueries),
        ["DeleteQueryWordTemplateCommand"] = ("queries.removeTemplate", CategoryQueries),
        ["SetDefaultWordTemplateCommand"] = ("queries.setDefaultTemplate", CategoryQueries),
        ["DeleteDefaultWordTemplateCommand"] = ("queries.removeDefaultTemplate", CategoryQueries),
        ["ImportQueriesCommand"] = (QueriesImport, CategoryQueries),

        ["CreateUserGroupCommand"] = ("userGroups.create", CategoryUserGroups),
        ["UpdateUserGroupCommand"] = ("userGroups.update", CategoryUserGroups),
        ["DeleteUserGroupCommand"] = ("userGroups.delete", CategoryUserGroups),
        ["SetUserGroupMembersCommand"] = ("userGroups.setMembers", CategoryUserGroups),

        ["CreateQueryGroupCommand"] = ("groups.create", CategoryGroups),
        ["UpdateQueryGroupCommand"] = ("groups.update", CategoryGroups),
        ["DeleteQueryGroupCommand"] = ("groups.delete", CategoryGroups),

        // Permission changes — the reason an auditor opens this page.
        ["AssignQueryToRolesCommand"] = ("access.queryRoles", CategoryAccess),
        ["AssignQueryToUserGroupsCommand"] = ("access.queryUserGroups", CategoryAccess),
        ["AssignQueryToUsersCommand"] = ("access.queryUsers", CategoryAccess),
        ["AssignQueryGroupToRolesCommand"] = ("access.groupRoles", CategoryAccess),
        ["AssignQueryGroupToUserGroupsCommand"] = ("access.groupUserGroups", CategoryAccess),
        ["AssignQueryGroupToUsersCommand"] = ("access.groupUsers", CategoryAccess),
        ["AssignDatabaseUserAccessCommand"] = ("access.databaseUser", CategoryAccess),

        ["CreateDatabaseUserCommand"] = ("databaseUsers.create", CategoryDatabaseUsers),
        ["UpdateDatabaseUserCommand"] = ("databaseUsers.update", CategoryDatabaseUsers),
        ["DeleteDatabaseUserCommand"] = ("databaseUsers.delete", CategoryDatabaseUsers),

        // Branding goes through MediatR, so the behavior picks it up; only the naming is here.
        // The logo bytes are dropped by the behavior's bulk-payload rule, not stored.
        ["SetBrandingLogoCommand"] = (BrandingLogoSet, CategoryBranding),
        ["DeleteBrandingLogoCommand"] = (BrandingLogoRemoved, CategoryBranding),

        ["CreateScheduledTaskCommand"] = ("scheduledTasks.create", CategoryScheduledTasks),
        ["UpdateScheduledTaskCommand"] = ("scheduledTasks.update", CategoryScheduledTasks),
        ["DeleteScheduledTaskCommand"] = ("scheduledTasks.delete", CategoryScheduledTasks),
        ["RunScheduledTaskNowCommand"] = ("scheduledTasks.runNow", CategoryScheduledTasks),
        ["CancelScheduledTaskRunCommand"] = ("scheduledTasks.cancelRun", CategoryScheduledTasks)
    };

    /// <summary>
    /// Commands that must not produce an audit row.
    ///
    /// <para>Login/refresh are authentication rather than administration, and run on every
    /// page load. Query execution already has its own richer log with parameters and row
    /// counts — duplicating it here would bury the administrative entries this page exists
    /// to show. Connection tests are read-only.</para>
    /// </summary>
    private static readonly HashSet<string> Excluded = new()
    {
        "LoginCommand",
        "RefreshTokenCommand",
        "ExecuteQueryCommand",
        "TestDatabaseConnectionCommand"
    };

    public static bool IsExcluded(string commandTypeName) => Excluded.Contains(commandTypeName);

    /// <summary>
    /// The code and category for a command type. Unmapped commands fall back to the type name
    /// with the "Command" suffix stripped, under the "other" category, so a newly added
    /// command is still recorded even before it is named here.
    /// </summary>
    public static (string Action, string Category) Resolve(string commandTypeName)
    {
        if (Map.TryGetValue(commandTypeName, out var mapped))
            return mapped;

        var trimmed = commandTypeName.EndsWith("Command", StringComparison.Ordinal)
            ? commandTypeName[..^"Command".Length]
            : commandTypeName;

        return (trimmed, CategoryOther);
    }

    /// <summary>Every code the UI has a translation for, so the client can be checked against it.</summary>
    public static IReadOnlyCollection<string> AllActions() =>
        Map.Values.Select(v => v.Action)
            .Concat(new[]
            {
                DirectoryImportUsers, DirectoryImportDepartment, DirectoryRevoke,
                DirectoryRestore, DirectorySync, BrandingLogoSet, BrandingLogoRemoved,
                SettingsUpdated, RolesCreate, RolesUpdate, RolesDelete, RolesSetPermissions
            })
            .Distinct()
            .ToList();
}
