namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity granting every member of a user group access to run a report.
/// Mirrors <see cref="DynamicQueryUserGroup"/>.
/// </summary>
public class ReportUserGroup
{
    public Guid ReportId { get; set; }
    public Report Report { get; set; } = null!;

    public Guid UserGroupId { get; set; }
    public UserGroup UserGroup { get; set; } = null!;
}
