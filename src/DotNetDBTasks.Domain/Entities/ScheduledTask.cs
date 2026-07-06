using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// An admin-defined recurring job that runs one or more read queries and writes each
/// result set to a file (Excel/CSV/JSON) in <see cref="OutputFolder"/> on the server.
/// Only admins create/edit/run tasks; other users see the task's status only when
/// listed in <see cref="Viewers"/>.
/// </summary>
public class ScheduledTask : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Nullable because Oracle stores an empty string as NULL.</summary>
    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Absolute server-side folder the export files are written to.</summary>
    public string OutputFolder { get; set; } = string.Empty;

    public ScheduleFrequency Frequency { get; set; }

    /// <summary>Minutes between runs. Used when <see cref="Frequency"/> is EveryNMinutes.</summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>Local time of day in "HH:mm". Used for Daily/Weekly/Monthly frequencies.</summary>
    public string? TimeOfDay { get; set; }

    /// <summary>0 = Sunday … 6 = Saturday. Used when <see cref="Frequency"/> is Weekly.</summary>
    public int? DayOfWeek { get; set; }

    /// <summary>1–31, clamped to the month's length. Used when <see cref="Frequency"/> is Monthly.</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>Next scheduled trigger, UTC. Null while the task is disabled.</summary>
    public DateTime? NextRunAt { get; set; }

    /// <summary>
    /// The admin who created the task. Scheduled runs execute under this user's
    /// identity (access checks and audit logs attribute to them).
    /// </summary>
    public Guid CreatedByUserId { get; set; }

    public ICollection<ScheduledTaskItem> Items { get; set; } = new List<ScheduledTaskItem>();
    public ICollection<ScheduledTaskRun> Runs { get; set; } = new List<ScheduledTaskRun>();
    public ICollection<ScheduledTaskViewer> Viewers { get; set; } = new List<ScheduledTaskViewer>();
}
