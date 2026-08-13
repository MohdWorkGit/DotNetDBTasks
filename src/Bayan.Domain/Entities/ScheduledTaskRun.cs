using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// History record of one execution of a scheduled task (triggered by the scheduler
/// or manually by an admin). Per-item outcomes are stored as JSON in
/// <see cref="ItemResultsJson"/>.
/// </summary>
public class ScheduledTaskRun : BaseEntity
{
    public Guid ScheduledTaskId { get; set; }
    public ScheduledTask ScheduledTask { get; set; } = null!;

    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ScheduledTaskRunStatus Status { get; set; }

    /// <summary>The admin who triggered a manual run; null when triggered by the scheduler.</summary>
    public Guid? TriggeredByUserId { get; set; }

    /// <summary>Denormalized for display so run history survives user changes.</summary>
    public string? TriggeredByUsername { get; set; }

    /// <summary>Task-level failure message (item-level errors live in <see cref="ItemResultsJson"/>).</summary>
    public string? Error { get; set; }

    /// <summary>
    /// JSON array of per-item results:
    /// [{"queryName","fileName","success","rowCount","error","durationMs"}, …].
    /// </summary>
    public string? ItemResultsJson { get; set; }
}
