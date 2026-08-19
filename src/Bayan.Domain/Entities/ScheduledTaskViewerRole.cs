namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity granting every holder of a role read-only visibility of a scheduled
/// task's status and run history (never the right to edit or trigger it).
///
/// <para>
/// The role-level counterpart of <see cref="ScheduledTaskViewer"/>. A user reaches the
/// task through whichever grant matches first — role, user group, or their own name —
/// and downloads when <em>any</em> matching grant allows it.
/// </para>
/// </summary>
public class ScheduledTaskViewerRole
{
    public Guid ScheduledTaskId { get; set; }
    public ScheduledTask ScheduledTask { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    /// <summary>
    /// When true the role's holders may also download the run's export files; plain
    /// viewers only see statuses and file names.
    /// </summary>
    public bool CanDownloadFiles { get; set; }
}
