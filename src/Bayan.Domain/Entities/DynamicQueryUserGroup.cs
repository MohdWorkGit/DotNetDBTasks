namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity representing access to a DynamicQuery granted to every member of a user group.
/// </summary>
public class DynamicQueryUserGroup
{
    public Guid DynamicQueryId { get; set; }
    public DynamicQuery DynamicQuery { get; set; } = null!;

    public Guid UserGroupId { get; set; }
    public UserGroup UserGroup { get; set; } = null!;
}
