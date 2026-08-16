using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Enums;
using Bayan.Domain.Interfaces;
using Bayan.Domain.Services;

namespace Bayan.API.BackgroundJobs;

/// <summary>
/// Drives scheduled export tasks. A polling loop finds enabled tasks whose
/// <c>NextRunAt</c> has passed, advances their next occurrence, and enqueues a run;
/// a consumer loop drains the queue (which also receives manual "run now" requests
/// from the admin controller) and executes each run on a fresh DI scope. Runs are
/// processed one at a time, so overlapping executions of the same task cannot happen.
/// </summary>
public class ScheduledTaskWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IScheduledTaskRunQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledTaskWorker> _logger;

    public ScheduledTaskWorker(
        IScheduledTaskRunQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledTaskWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Before anything can enqueue a new run: close out runs the previous process
        // was still executing when it died.
        await CloseOrphanedRunsAsync(stoppingToken);

        await Task.WhenAll(
            PollDueTasksAsync(stoppingToken),
            ConsumeRunsAsync(stoppingToken));
    }

    /// <summary>
    /// Marks runs left <see cref="ScheduledTaskRunStatus.Running"/> by a previous process as
    /// failed. A run row is written as Running before the work starts and only reaches a
    /// terminal status in the runner's finally block, so a process that is killed outright
    /// (an IIS app-pool recycle that outlasts the shutdown window, a crash, a power loss)
    /// strands the row. Nothing else ever reconciles it: the cancel endpoint resolves the run
    /// through the in-memory <c>IScheduledTaskRunRegistry</c>, which the new process starts
    /// empty, so the row would otherwise show as in-progress forever and refuse to cancel.
    /// </summary>
    private async Task CloseOrphanedRunsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var orphaned = await unitOfWork.ScheduledTaskRuns.FindAsync(
                r => r.Status == ScheduledTaskRunStatus.Running && r.CompletedAt == null,
                stoppingToken);

            if (orphaned.Count == 0)
                return;

            foreach (var run in orphaned)
            {
                run.Status = ScheduledTaskRunStatus.Failed;
                run.CompletedAt = DateTime.UtcNow;
                run.Error = "Interrupted by an application restart; the run did not complete.";
                unitOfWork.ScheduledTaskRuns.Update(run);
            }

            await unitOfWork.SaveChangesAsync(stoppingToken);

            // Worth a warning rather than information: every row here is work that silently
            // did not finish, and any incremental checkpoint it would have advanced did not
            // move either, so the next run re-exports from the previous key.
            _logger.LogWarning(
                "Closed {Count} scheduled task run(s) left in progress by a previous process",
                orphaned.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down before the sweep finished; the next start will redo it.
        }
        catch (Exception ex)
        {
            // Never block the scheduler from starting over a bookkeeping failure.
            _logger.LogError(ex, "Failed to close orphaned scheduled task runs");
        }
    }

    private async Task PollDueTasksAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                var now = DateTime.UtcNow;
                var dueTasks = await unitOfWork.ScheduledTasks.FindAsync(
                    t => t.IsEnabled && t.NextRunAt != null && t.NextRunAt <= now, stoppingToken, "Triggers");

                foreach (var task in dueTasks)
                {
                    // Advance NextRunAt before enqueueing so the next poll cannot
                    // re-trigger the same occurrence while this run is queued.
                    task.NextRunAt = ScheduleCalculator.ComputeNextRunUtc(task.IsEnabled, task.Triggers, DateTime.Now);
                    unitOfWork.ScheduledTasks.Update(task);
                    await unitOfWork.SaveChangesAsync(stoppingToken);

                    await _queue.EnqueueAsync(
                        new ScheduledTaskRunRequest(task.Id, null, null), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while polling for due scheduled tasks");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                return;
        }
    }

    private async Task ConsumeRunsAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IScheduledTaskRunner>();
                await runner.RunAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error executing scheduled task {TaskId}",
                    request.ScheduledTaskId);
            }
        }
    }
}
