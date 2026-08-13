using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// One recurrence rule of a scheduled task. A task can have several triggers
/// (e.g. "daily at 03:00" plus "monthly on day 14 at 09:00" plus "daily at 14:00");
/// it fires on the earliest upcoming occurrence across all of them.
/// </summary>
public class ScheduledTaskTrigger : BaseEntity
{
    public Guid ScheduledTaskId { get; set; }
    public ScheduledTask ScheduledTask { get; set; } = null!;

    public ScheduleFrequency Frequency { get; set; }

    /// <summary>Minutes between runs. Used when <see cref="Frequency"/> is EveryNMinutes.</summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>Local time of day in "HH:mm". Used for Daily/Weekly/Monthly frequencies.</summary>
    public string? TimeOfDay { get; set; }

    /// <summary>0 = Sunday … 6 = Saturday. Used when <see cref="Frequency"/> is Weekly.</summary>
    public int? DayOfWeek { get; set; }

    /// <summary>1–31, clamped to the month's length. Used when <see cref="Frequency"/> is Monthly.</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>Display order in the admin form.</summary>
    public int SortOrder { get; set; }
}
