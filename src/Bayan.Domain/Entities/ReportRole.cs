namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity granting every member of a role access to run a report.
/// Mirrors <see cref="DynamicQueryRole"/>.
/// </summary>
public class ReportRole
{
    public Guid ReportId { get; set; }
    public Report Report { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
