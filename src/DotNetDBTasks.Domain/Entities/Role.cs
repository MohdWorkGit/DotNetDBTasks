namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Represents a system role used for authorization and query assignment.
/// </summary>
public class Role : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<DynamicQueryRole> DynamicQueryRoles { get; set; } = new List<DynamicQueryRole>();
}
