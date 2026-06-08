using DotNetDBTasks.Application.Common.Interfaces;
using MediatR;

namespace DotNetDBTasks.API.BackgroundJobs;

/// <summary>
/// Drains the query job queue and executes each job on a fresh DI scope, off the HTTP
/// request thread. Up to <see cref="MaxConcurrency"/> jobs run in parallel; the rest wait
/// in the queue. Each job replays its <c>ExecuteQueryCommand</c> through MediatR exactly as
/// the synchronous path did, so access control and audit logging are unchanged.
/// </summary>
public class QueryJobWorker : BackgroundService
{
    private const int MaxConcurrency = 4;

    private readonly IQueryJobQueue _queue;
    private readonly IQueryJobStore _jobStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueryJobWorker> _logger;
    private readonly SemaphoreSlim _semaphore = new(MaxConcurrency, MaxConcurrency);

    public QueryJobWorker(
        IQueryJobQueue queue,
        IQueryJobStore jobStore,
        IServiceScopeFactory scopeFactory,
        ILogger<QueryJobWorker> logger)
    {
        _queue = queue;
        _jobStore = jobStore;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in _queue.ReadAllAsync(stoppingToken))
        {
            await _semaphore.WaitAsync(stoppingToken);
            // Fire-and-forget: ProcessJobAsync handles its own errors and releases the slot.
            _ = ProcessJobAsync(jobId, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        try
        {
            var job = _jobStore.Get(jobId);
            if (job is null || job.Status == QueryJobStatus.Canceled)
                return;

            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var userContext = scope.ServiceProvider.GetRequiredService<IUserExecutionContext>();

            // Seed the ambient user so the unchanged handler's access checks resolve off-thread.
            // Set inside this per-job async flow so the AsyncLocal stays isolated per job.
            userContext.Current = job.UserSnapshot;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken, job.Cts.Token);

            _jobStore.Update(jobId, j => j.Status = QueryJobStatus.Running);

            try
            {
                var result = await mediator.Send(job.Command, linked.Token);
                _jobStore.Update(jobId, j =>
                {
                    j.Result = result;
                    j.Status = QueryJobStatus.Succeeded;
                });
            }
            catch (OperationCanceledException) when (job.Cts.IsCancellationRequested)
            {
                _jobStore.Update(jobId, j =>
                {
                    j.Status = QueryJobStatus.Canceled;
                    j.Error = "Query was canceled.";
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Query job {JobId} failed", jobId);
                _jobStore.Update(jobId, j =>
                {
                    j.Status = QueryJobStatus.Failed;
                    j.Error = ex.Message;
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing query job {JobId}", jobId);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
