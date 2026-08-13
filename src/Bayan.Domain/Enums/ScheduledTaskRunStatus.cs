namespace Bayan.Domain.Enums;

/// <summary>
/// Outcome of a single scheduled task run.
/// </summary>
public enum ScheduledTaskRunStatus
{
    Running = 0,

    /// <summary>Every query item exported successfully.</summary>
    Succeeded = 1,

    /// <summary>At least one item exported and at least one failed.</summary>
    PartiallySucceeded = 2,

    /// <summary>No item exported successfully.</summary>
    Failed = 3,

    /// <summary>An admin cancelled the run while it was in progress (the running query was aborted).</summary>
    Canceled = 4
}
