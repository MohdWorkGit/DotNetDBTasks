namespace Bayan.Domain.Entities;

/// <summary>
/// Join entity granting every member of a user group read-only visibility of a
/// scheduled task's status and run history (never the right to edit or trigger it).
///
/// <para>
/// The group-level counterpart of <see cref="ScheduledTaskViewer"/>. Membership is
/// read at access-check time, so adding someone to the group grants them the task
/// immediately and removing them revokes it.
/// </para>
/// </summary>
public class ScheduledTaskViewerUserGroup
{
    public Guid ScheduledTaskId { get; set; }
    public ScheduledTask ScheduledTask { get; set; } = null!;

    public Guid UserGroupId { get; set; }
    public UserGroup UserGroup { get; set; } = null!;

    /// <summary>
    /// When true the group's members may also download the run's export files; plain
    /// viewers only see statuses and file names.
    /// </summary>
    public bool CanDownloadFiles { get; set; }
}
