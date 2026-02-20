namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Join entity representing the many-to-many relationship between DynamicQueries and Roles.
/// Controls which roles can execute which queries.
/// </summary>
public class DynamicQueryRole
{
    public Guid DynamicQueryId { get; set; }
    public DynamicQuery DynamicQuery { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
