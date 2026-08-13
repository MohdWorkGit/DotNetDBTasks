namespace Bayan.Application.Common.Interfaces;

/// <summary>
/// Executes one scheduled task end-to-end: runs each query item under the task
/// creator's identity, exports every result to a file in the task's output folder,
/// and records a run history row. Resolved from a fresh DI scope per run.
/// </summary>
public interface IScheduledTaskRunner
{
    Task RunAsync(ScheduledTaskRunRequest request, CancellationToken cancellationToken);
}
