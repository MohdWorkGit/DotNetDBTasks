using System.Collections.Concurrent;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Features.QueryExecution.Commands;
using Microsoft.Extensions.Configuration;

namespace DotNetDBTasks.Infrastructure.BackgroundJobs;

/// <summary>
/// In-memory <see cref="IQueryJobStore"/> backed by a ConcurrentDictionary. Registered as
/// a singleton. Jobs (and their cached results) use a <b>sliding</b> retention: a job is evicted
/// only after it has been idle for <c>ResultCache:RetentionMinutes</c> (default 1 day), so an
/// actively-viewed result stays cached. The client also releases a job explicitly when it leaves
/// the results page. Suitable for a single API instance; a multi-instance deployment would need a
/// shared/persistent store instead.
/// </summary>
public class InMemoryQueryJobStore : IQueryJobStore
{
    private readonly TimeSpan _retention;

    private readonly ConcurrentDictionary<Guid, QueryJob> _jobs = new();

    public InMemoryQueryJobStore(IConfiguration configuration)
    {
        var minutes = configuration.GetValue<int>("ResultCache:RetentionMinutes", 1440);
        if (minutes <= 0) minutes = 1440;
        _retention = TimeSpan.FromMinutes(minutes);
    }

    public QueryJob Create(Guid userId, UserContextSnapshot snapshot, ExecuteQueryCommand command)
    {
        Sweep();

        var now = DateTime.UtcNow;
        var job = new QueryJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserSnapshot = snapshot,
            Command = command,
            Status = QueryJobStatus.Queued,
            CreatedAt = now,
            LastAccessedAt = now
        };

        _jobs[job.Id] = job;
        return job;
    }

    public QueryJob? Get(Guid id)
    {
        if (!_jobs.TryGetValue(id, out var job))
            return null;

        // Touch: sliding retention keeps a result alive while it is still being read.
        job.LastAccessedAt = DateTime.UtcNow;
        return job;
    }

    public void Update(Guid id, Action<QueryJob> mutate)
    {
        if (_jobs.TryGetValue(id, out var job))
            mutate(job);
    }

    public void Remove(Guid id)
    {
        if (_jobs.TryRemove(id, out var removed))
            removed.Cts.Dispose();
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

    /// <summary>Drops jobs idle longer than the retention window. Called opportunistically on Create.</summary>
    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - _retention;
        foreach (var kvp in _jobs)
        {
            if (kvp.Value.LastAccessedAt < cutoff && _jobs.TryRemove(kvp.Key, out var removed))
                removed.Cts.Dispose();
        }
    }
}
