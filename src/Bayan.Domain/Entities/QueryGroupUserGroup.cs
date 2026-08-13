namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity granting every query inside a query group to every member of a user group.
/// </summary>
public class QueryGroupUserGroup
{
    public Guid QueryGroupId { get; set; }
    public QueryGroup QueryGroup { get; set; } = null!;

    public Guid UserGroupId { get; set; }
    public UserGroup UserGroup { get; set; } = null!;
}
