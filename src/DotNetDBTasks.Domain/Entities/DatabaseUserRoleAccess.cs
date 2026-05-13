namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Join entity controlling which roles are allowed to use which database users.
/// Only users belonging to a role with an entry here (or Admins) can execute queries
/// through the associated DatabaseUser.
/// </summary>
public class DatabaseUserRoleAccess
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public Guid DatabaseUserId { get; set; }
    public DatabaseUser DatabaseUser { get; set; } = null!;
}
