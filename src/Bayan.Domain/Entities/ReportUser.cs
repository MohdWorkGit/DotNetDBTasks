namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity granting a named user access to run a report.
/// Mirrors <see cref="DynamicQueryUser"/>.
/// </summary>
public class ReportUser
{
    public Guid ReportId { get; set; }
    public Report Report { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
