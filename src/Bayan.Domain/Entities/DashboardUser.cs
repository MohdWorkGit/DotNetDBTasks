namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity granting a named user access to view a dashboard.
/// Mirrors <see cref="ReportUser"/>.
/// </summary>
public class DashboardUser
{
    public Guid DashboardId { get; set; }
    public Dashboard Dashboard { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
