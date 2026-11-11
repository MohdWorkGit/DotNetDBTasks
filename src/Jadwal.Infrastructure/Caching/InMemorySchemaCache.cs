using System.Collections.Concurrent;
using Jadwal.Application.Common.Interfaces;

namespace Jadwal.Infrastructure.Caching;

/// <summary>
/// <see cref="ISchemaCache"/> in memory, on the pattern of <see cref="InMemoryDashboardTileCache"/>:
/// one load at a time per connection (the <see cref="Lazy{T}"/>), a failed load never kept, and
/// an entry older than <see cref="Lifetime"/> read again. A connection's entry is dropped when the
/// connection is edited or deleted, and on "Refresh structure".
/// </summary>
public sealed class InMemorySchemaCache : ISchemaCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private sealed record Entry(Lazy<Task<IReadOnlyList<SchemaObject>>> Load, DateTime LoadedAt);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public async Task<IReadOnlyList<SchemaObject>> GetOrAddAsync(
        Guid connectionKey, Func<Task<IReadOnlyList<SchemaObject>>> load, bool refresh)
    {
        if (refresh)
            _entries.TryRemove(connectionKey, out _);

        var entry = _entries.AddOrUpdate(
            connectionKey,
            _ => NewEntry(load),
            (_, existing) => IsUsable(existing) ? existing : NewEntry(load));

        try
        {
            return await entry.Load.Value;
        }
        catch
        {
            // Never hand the same failure to the next caller: the next request reads again.
            _entries.TryRemove(new KeyValuePair<Guid, Entry>(connectionKey, entry));
            throw;
        }
    }

    public void Invalidate(Guid connectionKey) => _entries.TryRemove(connectionKey, out _);

    private static Entry NewEntry(Func<Task<IReadOnlyList<SchemaObject>>> load) =>
        new(new Lazy<Task<IReadOnlyList<SchemaObject>>>(load, LazyThreadSafetyMode.ExecutionAndPublication), DateTime.UtcNow);

    private static bool IsUsable(Entry entry) =>
        DateTime.UtcNow - entry.LoadedAt < Lifetime
        && !(entry.Load.IsValueCreated && entry.Load.Value.IsFaulted);
}
