namespace DotNetDBTasks.Domain.Enums;

/// <summary>
/// How often a scheduled task recurs. Frequency-specific fields on the task
/// (interval, time of day, day of week/month) qualify the chosen frequency.
/// </summary>
public enum ScheduleFrequency
{
    /// <summary>Runs every <c>IntervalMinutes</c> minutes.</summary>
    EveryNMinutes = 0,

    /// <summary>Runs once a day at <c>TimeOfDay</c>.</summary>
    Daily = 1,

    /// <summary>Runs once a week on <c>DayOfWeek</c> at <c>TimeOfDay</c>.</summary>
    Weekly = 2,

    /// <summary>Runs once a month on <c>DayOfMonth</c> at <c>TimeOfDay</c>.</summary>
    Monthly = 3
}
