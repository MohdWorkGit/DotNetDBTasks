namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// A named set of permissions, and the unit queries and connections are granted to.
///
/// <para>Roles are rows, not an enum: an administrator can add one on Settings → Permissions
/// and decide what it may do. The four seeded names still exist, and <c>Admin</c> is special —
/// it holds every permission and cannot be edited or deleted.</para>
/// </summary>
public class Role : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// True for the four roles the system seeds. They may be re-permissioned like any other,
    /// but not renamed or deleted: the seeder recreates them, and code and translations refer
    /// to them by name.
    /// </summary>
    public bool IsSeeded { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<DynamicQueryRole> DynamicQueryRoles { get; set; } = new List<DynamicQueryRole>();
    public ICollection<DatabaseUserRoleAccess> DatabaseUserAccess { get; set; } = new List<DatabaseUserRoleAccess>();
}
