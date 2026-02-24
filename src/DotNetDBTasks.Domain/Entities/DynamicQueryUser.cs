namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Join entity representing access to a DynamicQuery granted to a specific user.
/// </summary>
public class DynamicQueryUser
{
    public Guid DynamicQueryId { get; set; }
    public DynamicQuery DynamicQuery { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
