namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Join entity granting every query inside a group access to all users in a role.
/// </summary>
public class QueryGroupRole
{
    public Guid QueryGroupId { get; set; }
    public QueryGroup QueryGroup { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
