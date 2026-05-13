namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Join entity granting every query inside a group access to a specific user.
/// </summary>
public class QueryGroupUser
{
    public Guid QueryGroupId { get; set; }
    public QueryGroup QueryGroup { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
