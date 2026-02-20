namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Base entity providing common audit fields for all domain entities.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
