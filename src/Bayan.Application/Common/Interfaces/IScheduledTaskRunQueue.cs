namespace Bayan.Application.Common.Interfaces;

/// <summary>
/// A request to execute a scheduled task once. <paramref name="TriggeredByUserId"/> /
/// <paramref name="TriggeredByUsername"/> identify the admin behind a manual run;
/// both are null for scheduler-triggered runs.
/// </summary>
public record ScheduledTaskRunRequest(Guid ScheduledTaskId, Guid? TriggeredByUserId, string? TriggeredByUsername);

/// <summary>
/// Producer/consumer queue of pending scheduled-task runs. The controller (manual run)
/// and the scheduler loop (due tasks) enqueue; the background worker drains it.
/// </summary>
public interface IScheduledTaskRunQueue
{
    ValueTask EnqueueAsync(ScheduledTaskRunRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<ScheduledTaskRunRequest> ReadAllAsync(CancellationToken cancellationToken);
}
