using Bayan.Domain.Entities;
using Bayan.Domain.Enums;

namespace Bayan.Domain.Services;

/// <summary>
/// Pure computation of a scheduled task's next trigger time. Schedules are expressed
/// in server-local time (an admin's "daily at 07:00" means 07:00 on the server clock);
/// the returned value is converted to UTC for storage/comparison.
/// </summary>
public static class ScheduleCalculator
{
    /// <summary>
    /// Returns the earliest next occurrence across all of a task's triggers, strictly
    /// after <paramref name="afterLocal"/> (local time), as a UTC timestamp. Returns
    /// null when the task is disabled or has no triggers.
    /// </summary>
    public static DateTime? ComputeNextRunUtc(
        bool isEnabled, IEnumerable<ScheduledTaskTrigger> triggers, DateTime afterLocal)
    {
        if (!isEnabled)
            return null;

        DateTime? earliest = null;
        foreach (var trigger in triggers)
        {
            var next = ComputeNextRunUtc(trigger, afterLocal);
            if (next is not null && (earliest is null || next < earliest))
                earliest = next;
        }
        return earliest;
    }

    /// <summary>
    /// Returns the trigger's next occurrence strictly after <paramref name="afterLocal"/>
    /// (local time), as a UTC timestamp.
    /// </summary>
    public static DateTime? ComputeNextRunUtc(ScheduledTaskTrigger trigger, DateTime afterLocal)
    {
        var time = ParseTimeOfDay(trigger.TimeOfDay);

        DateTime nextLocal;
        switch (trigger.Frequency)
        {
            case ScheduleFrequency.EveryNMinutes:
                var interval = Math.Max(1, trigger.IntervalMinutes ?? 60);
                nextLocal = afterLocal.AddMinutes(interval);
                break;

            case ScheduleFrequency.Daily:
                nextLocal = afterLocal.Date + time;
                if (nextLocal <= afterLocal)
                    nextLocal = nextLocal.AddDays(1);
                break;

            case ScheduleFrequency.Weekly:
                var targetDay = (DayOfWeek)Math.Clamp(trigger.DayOfWeek ?? 1, 0, 6);
                var daysAhead = ((int)targetDay - (int)afterLocal.DayOfWeek + 7) % 7;
                nextLocal = afterLocal.Date.AddDays(daysAhead) + time;
                if (nextLocal <= afterLocal)
                    nextLocal = nextLocal.AddDays(7);
                break;

            case ScheduleFrequency.Monthly:
                var day = Math.Clamp(trigger.DayOfMonth ?? 1, 1, 31);
                nextLocal = MonthlyOccurrence(afterLocal.Year, afterLocal.Month, day, time);
                if (nextLocal <= afterLocal)
                {
                    var next = afterLocal.AddMonths(1);
                    nextLocal = MonthlyOccurrence(next.Year, next.Month, day, time);
                }
                break;

            default:
                return null;
        }

        return DateTime.SpecifyKind(nextLocal, DateTimeKind.Local).ToUniversalTime();
    }

    /// <summary>Day-of-month is clamped to the month's length (e.g. 31 → Feb 28).</summary>
    private static DateTime MonthlyOccurrence(int year, int month, int day, TimeSpan time) =>
        new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month))) + time;

    /// <summary>Parses "HH:mm"; blank or malformed values fall back to midnight.</summary>
    public static TimeSpan ParseTimeOfDay(string? timeOfDay) =>
        TimeSpan.TryParse(timeOfDay, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)
            ? t
            : TimeSpan.Zero;
}
