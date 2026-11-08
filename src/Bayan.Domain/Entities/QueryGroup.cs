namespace Bayan.Domain.Entities;

/// <summary>
/// A named folder that groups related DynamicQueries together. On the user-facing
/// "My Queries" page, queries are organised by their group. Groups themselves
/// have their own role/user-group/user access assignments — granting group access
/// is a shortcut for granting access to every query inside the group.
/// </summary>
public class QueryGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Guid CreatedByUserId { get; set; }

    /// <summary>The queries filed here. A query may be filed in several groups at once.</summary>
    public ICollection<QueryGroupQuery> QueryGroupQueries { get; set; } = new List<QueryGroupQuery>();
    public ICollection<QueryGroupRole> QueryGroupRoles { get; set; } = new List<QueryGroupRole>();
    public ICollection<QueryGroupUserGroup> QueryGroupUserGroups { get; set; } = new List<QueryGroupUserGroup>();
    public ICollection<QueryGroupUser> QueryGroupUsers { get; set; } = new List<QueryGroupUser>();
}
