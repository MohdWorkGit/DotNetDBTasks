namespace DotNetDBTasks.Application.Features.UserGroups.Queries;

/// <summary>
/// A user group with its membership. Groups are small enough to list whole — the picker on
/// the access pages and the members editor are both driven from this.
/// </summary>
public class UserGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int MemberCount { get; set; }
    public List<UserGroupMemberDto> Members { get; set; } = new();
}

public class UserGroupMemberDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
