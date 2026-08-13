using System.Collections.Concurrent;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;
using Bayan.Application.Features.QueryExecution.Commands;
using Bayan.Infrastructure.Caching;
using Microsoft.Extensions.Configuration;

namespace Bayan.Infrastructure.BackgroundJobs;

/// <summary>
/// In-memory <see cref="IQueryJobStore"/> backed by a ConcurrentDictionary. Registered as a
/// singleton. A finished read result keeps only metadata in the heap; its rows are held by an
/// <see cref="ICachedResult"/> that stays in memory when small and spills to local disk when large
/// (see <see cref="ResultSpillWriter"/>). Retention is <b>sliding</b> — a job is evicted after it
/// has been idle for <c>ResultCache:RetentionMinutes</c> — and a background maintenance sweep also
/// enforces heap/disk size budgets via LRU eviction, so memory stays bounded under load. Spill files
/// are deleted on eviction, on explicit removal, at startup (orphans from a prior run) and at
/// shutdown. Suitable for a single API instance; a multi-instance deployment would need a
/// shared/persistent store instead.
/// </summary>
public class InMemoryQueryJobStore : IQueryJobStore, IDisposable
{
    private readonly ResultCacheOptions _options;
    private readonly ConcurrentDictionary<Guid, QueryJob> _jobs = new();

    public InMemoryQueryJobStore(IConfiguration configuration)
    {
        _options = ResultCacheOptions.FromConfiguration(configuration);
        CleanSpillDirectory(); // remove any orphans left by a previous (possibly crashed) run
    }

    public TimeSpan MaintenanceInterval => _options.MaintenanceInterval;

    public QueryJob Create(Guid userId, UserContextSnapshot snapshot, ExecuteQueryCommand command)
    {
        EvictIdle(); // cheap opportunistic pass; the maintenance service also runs on a timer

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

    public void SetResult(Guid id, QueryExecutionResult result)
    {
        if (!_jobs.TryGetValue(id, out var job))
            return;

        job.Result = result;
        job.LastAccessedAt = DateTime.UtcNow;

        // Only read results carry rows worth caching; write/preview results stay as-is in memory.
        var isReadResult = !result.RequiresConfirmation && result.Columns.Count > 0;
        if (isReadResult)
        {
            job.CachedRows?.Dispose();
            job.CachedRows = BuildCachedResult(id, result.Columns, result.Rows);

            // Detach the large row list so only metadata remains on QueryExecutionResult.
            result.Rows = new List<Dictionary<string, object?>>();
        }

        // Publish Succeeded only after CachedRows is ready, so a poller never sees a "ready" job
        // whose rows/export would momentarily 409.
        job.Status = QueryJobStatus.Succeeded;
    }

    private ICachedResult BuildCachedResult(
        Guid id,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        try
        {
            return ResultSpillWriter.Create(id, columns, rows, _options);
        }
        catch
        {
            // Spilling failed (e.g. disk full/permissions) — degrade to keeping it in memory rather
            // than losing the result.
            var estimate = (long)rows.Count * Math.Max(1, columns.Count) * 32;
            return new InMemoryCachedResult(columns, rows, estimate);
        }
    }

    public void Remove(Guid id)
    {
        if (_jobs.TryRemove(id, out var removed))
            DisposeJob(removed);
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

    public void RunMaintenance()
    {
        EvictIdle();
        EvictToBudget();
    }

    /// <summary>Evicts terminal jobs idle longer than the retention window. Never touches queued/running jobs.</summary>
    private void EvictIdle()
    {
        var cutoff = DateTime.UtcNow - _options.Retention;
        foreach (var kvp in _jobs)
        {
            if (IsTerminal(kvp.Value.Status) && kvp.Value.LastAccessedAt < cutoff)
                Evict(kvp.Key);
        }
    }

    /// <summary>
    /// Enforces the heap and disk budgets by evicting the least-recently-accessed terminal results
    /// until each total is back under its cap. Queued/running jobs are never evicted.
    /// </summary>
    private void EvictToBudget()
    {
        EnforceBudget(onDisk: false, _options.MaxHeapBytes);
        EnforceBudget(onDisk: true, _options.MaxDiskBytes);
    }

    private void EnforceBudget(bool onDisk, long budget)
    {
        long Total() => _jobs.Values
            .Where(j => IsTerminal(j.Status) && j.CachedRows is { } c && c.IsOnDisk == onDisk)
            .Sum(j => j.CachedRows!.ApproxSizeBytes);

        if (Total() <= budget)
            return;

        var candidates = _jobs.Values
            .Where(j => IsTerminal(j.Status) && j.CachedRows is { } c && c.IsOnDisk == onDisk)
            .OrderBy(j => j.LastAccessedAt)
            .ToList();

        foreach (var job in candidates)
        {
            if (Total() <= budget)
                break;
            Evict(job.Id);
        }
    }

    private void Evict(Guid id)
    {
        if (_jobs.TryRemove(id, out var removed))
            DisposeJob(removed);
    }

    private static void DisposeJob(QueryJob job)
    {
        job.CachedRows?.Dispose();
        job.Cts.Dispose();
    }

    private static bool IsTerminal(QueryJobStatus status) =>
        status is QueryJobStatus.Succeeded or QueryJobStatus.Failed or QueryJobStatus.Canceled;

    private void CleanSpillDirectory()
    {
        try
        {
            if (!Directory.Exists(_options.SpillDirectory))
                return;
            foreach (var file in Directory.EnumerateFiles(_options.SpillDirectory))
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        foreach (var kvp in _jobs)
        {
            if (_jobs.TryRemove(kvp.Key, out var removed))
                DisposeJob(removed);
        }
        CleanSpillDirectory();
        GC.SuppressFinalize(this);
    }
}
