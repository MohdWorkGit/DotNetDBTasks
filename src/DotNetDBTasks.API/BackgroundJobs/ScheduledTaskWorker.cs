using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Interfaces;
using DotNetDBTasks.Domain.Services;

namespace DotNetDBTasks.API.BackgroundJobs;

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
        await Task.WhenAll(
            PollDueTasksAsync(stoppingToken),
            ConsumeRunsAsync(stoppingToken));
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
