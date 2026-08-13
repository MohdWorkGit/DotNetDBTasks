namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity representing the many-to-many relationship between Users and Roles.
/// </summary>
public class UserRole
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
