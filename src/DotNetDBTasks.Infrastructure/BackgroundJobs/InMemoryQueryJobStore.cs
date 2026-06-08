using System.Collections.Concurrent;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Features.QueryExecution.Commands;

namespace DotNetDBTasks.Infrastructure.BackgroundJobs;

/// <summary>
/// In-memory <see cref="IQueryJobStore"/> backed by a ConcurrentDictionary. Registered as
/// a singleton. Completed jobs (and their results) are evicted after <see cref="Retention"/>
/// so large result sets don't accumulate. Suitable for a single API instance; a multi-instance
/// deployment would need a shared/persistent store instead.
/// </summary>
public class InMemoryQueryJobStore : IQueryJobStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, QueryJob> _jobs = new();

    public QueryJob Create(Guid userId, UserContextSnapshot snapshot, ExecuteQueryCommand command)
    {
        Sweep();

        var job = new QueryJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserSnapshot = snapshot,
            Command = command,
            Status = QueryJobStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };

        _jobs[job.Id] = job;
        return job;
    }

    public QueryJob? Get(Guid id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public void Update(Guid id, Action<QueryJob> mutate)
    {
        if (_jobs.TryGetValue(id, out var job))
            mutate(job);
    }

    public bool Cancel(Guid id)
    {
        if (!_jobs.TryGetValue(id, out var job))
            return false;

        if (job.Status is QueryJobStatus.Succeeded or QueryJobStatus.Failed or QueryJobStatus.Canceled)
            return false;

        job.Status = QueryJobStatus.Canceled;
        try
        {
            job.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Worker already disposed the source after completing — nothing to cancel.
        }
        return true;
    }

    /// <summary>Drops jobs older than the retention window. Called opportunistically on Create.</summary>
    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var kvp in _jobs)
        {
            if (kvp.Value.CreatedAt < cutoff && _jobs.TryRemove(kvp.Key, out var removed))
                removed.Cts.Dispose();
        }
    }
}
