namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Join entity granting a non-admin user permission to view a scheduled task's
/// status and run history (read-only — never to edit or trigger it).
/// </summary>
public class ScheduledTaskViewer
{
    public Guid ScheduledTaskId { get; set; }
    public ScheduledTask ScheduledTask { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// When true the viewer may also download the run's export files; plain viewers
    /// only see statuses and file names. Admins can always download; Auditors cannot.
    /// </summary>
    public bool CanDownloadFiles { get; set; }
}
