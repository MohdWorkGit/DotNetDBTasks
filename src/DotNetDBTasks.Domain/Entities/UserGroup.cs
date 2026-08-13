namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// A named group of users maintained inside the application, and the unit access is granted
/// to when it is granted to more than one person.
///
/// <para>
/// This deliberately does not mirror the directory. A user's AD department is a fact about
/// the directory that the application only records (see <see cref="User.Department"/>) — it
/// grants nothing. Membership here is set by an administrator, so access survives a
/// reorganisation in AD, works for locally created accounts that have no directory entry at
/// all, and can be read off one page rather than inferred from an attribute nobody here owns.
/// </para>
/// </summary>
public class UserGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Guid CreatedByUserId { get; set; }

    public ICollection<UserGroupMember> Members { get; set; } = new List<UserGroupMember>();
    public ICollection<DynamicQueryUserGroup> DynamicQueryUserGroups { get; set; } = new List<DynamicQueryUserGroup>();
    public ICollection<QueryGroupUserGroup> QueryGroupUserGroups { get; set; } = new List<QueryGroupUserGroup>();
}
