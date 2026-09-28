namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity granting every member of a role access to view a dashboard.
/// Mirrors <see cref="ReportRole"/>.
/// </summary>
public class DashboardRole
{
    public Guid DashboardId { get; set; }
    public Dashboard Dashboard { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
