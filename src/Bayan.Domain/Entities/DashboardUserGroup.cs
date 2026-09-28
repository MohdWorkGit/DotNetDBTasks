namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity granting every member of a user group access to view a dashboard.
/// Mirrors <see cref="ReportUserGroup"/>.
/// </summary>
public class DashboardUserGroup
{
    public Guid DashboardId { get; set; }
    public Dashboard Dashboard { get; set; } = null!;

    public Guid UserGroupId { get; set; }
    public UserGroup UserGroup { get; set; } = null!;
}
