namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity placing a user in a <see cref="UserGroup"/>. Membership is what turns a
/// grant made to the group into access for the person.
/// </summary>
public class UserGroupMember
{
    public Guid UserGroupId { get; set; }
    public UserGroup UserGroup { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
