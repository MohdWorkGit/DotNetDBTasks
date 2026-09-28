using System.Collections.Concurrent;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Features.Dashboards.Dtos;

namespace Bayan.Infrastructure.Caching;

/// <summary>
/// Process-local tile cache. Each entry is the <i>task</i> producing a result rather than the
/// result itself, which is what makes it single-flight: the first viewer to miss starts the query,
/// and everyone who asks while it runs awaits that same task instead of starting their own.
///
/// <para>Tile results are small — a chart's categories, a KPI's few numbers, a table tile's first
/// hundred rows — so they live on the heap with no disk spill, unlike the query result cache.
/// A cap bounds them anyway, since every distinct combination of filter values is an entry.</para>
///
/// <para>Per process: on a web farm each node warms its own, which costs one query per node per
/// interval — still bounded, and nothing a viewer could notice.</para>
/// </summary>
public sealed class InMemoryDashboardTileCache : IDashboardTileCache
{
    /// <summary>Entries kept at most. Past this, expired entries go first, then the oldest.</summary>
    internal const int MaxEntries = 500;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    private sealed class Entry
    {
        public required Guid DashboardId { get; init; }
        public required Lazy<Task<DashboardTileDataDto>> Value { get; init; }
        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        /// <summary>Fresh while running, and until the result's own refresh time once it has one.</summary>
        public bool IsFresh(DateTime now)
        {
            var task = Value.Value;
            if (!task.IsCompleted)
                return true;

            // A factory that threw is never reused; the next caller runs it again.
            return task.IsCompletedSuccessfully && task.Result.NextRefreshAt > now;
        }
    }

    public Task<DashboardTileDataDto> GetOrCreateAsync(
        Guid dashboardId,
        string key,
        Func<Task<DashboardTileDataDto>> factory)
    {
        var fullKey = $"{dashboardId:N}:{key}";

        while (true)
        {
            var now = DateTime.UtcNow;

            if (_entries.TryGetValue(fullKey, out var existing))
            {
                if (existing.IsFresh(now))
                    return existing.Value.Value;

                // Stale: replace it, but only if nobody else already has. Losing that race means
                // another caller installed a fresh entry, which the next loop turn will find.
                var replacement = NewEntry(dashboardId, factory);
                if (_entries.TryUpdate(fullKey, replacement, existing))
                    return replacement.Value.Value;

                continue;
            }

            var entry = NewEntry(dashboardId, factory);
            if (_entries.TryAdd(fullKey, entry))
            {
                TrimIfNeeded();
                return entry.Value.Value;
            }
        }
    }

    public void InvalidateDashboard(Guid dashboardId)
    {
        foreach (var pair in _entries)
        {
            if (pair.Value.DashboardId == dashboardId)
                _entries.TryRemove(pair);
        }
    }

    private static Entry NewEntry(Guid dashboardId, Func<Task<DashboardTileDataDto>> factory) => new()
    {
        DashboardId = dashboardId,
        // ExecutionAndPublication: exactly one thread runs the factory for this entry.
        Value = new Lazy<Task<DashboardTileDataDto>>(factory, LazyThreadSafetyMode.ExecutionAndPublication)
    };

    private void TrimIfNeeded()
    {
        if (_entries.Count <= MaxEntries)
            return;

        var now = DateTime.UtcNow;
        foreach (var pair in _entries)
        {
            if (pair.Value.Value.IsValueCreated && !pair.Value.IsFresh(now))
                _entries.TryRemove(pair);
        }

        var excess = _entries.Count - MaxEntries;
        if (excess <= 0)
            return;

        // Still over: drop the oldest. An in-flight entry dropped here still completes for the
        // callers already awaiting it; it simply is not found by the next one.
        foreach (var pair in _entries.OrderBy(p => p.Value.CreatedAt).Take(excess).ToList())
            _entries.TryRemove(pair);
    }
}
