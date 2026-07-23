using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Models;

namespace DotNetDBTasks.Infrastructure.Caching;

/// <summary>
/// A small cached result whose rows stay in the managed heap. Paging/sorting/filtering run over
/// the in-memory list; nothing touches disk.
/// </summary>
internal sealed class InMemoryCachedResult : ICachedResult
{
    private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _rows;

    public InMemoryCachedResult(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        long approxSizeBytes)
    {
        Columns = columns;
        _rows = rows;
        ApproxSizeBytes = approxSizeBytes;
    }

    public IReadOnlyList<string> Columns { get; }
    public int TotalRows => _rows.Count;
    public long ApproxSizeBytes { get; }
    public bool IsOnDisk => false;

    public CachedResultPage GetPage(
        int pageIndex,
        int pageSize,
        string? sortColumn,
        string? sortDir,
        IReadOnlyDictionary<string, string> filters)
    {
        if (pageSize <= 0) pageSize = 25;
        if (pageIndex < 0) pageIndex = 0;

        // Keep the original index so ties order stably (matching the disk path, which ties on offset).
        var filtered = new List<(IReadOnlyDictionary<string, object?> Row, int Index)>();
        for (int i = 0; i < _rows.Count; i++)
        {
            if (CachedRowOps.Matches(_rows[i], filters))
                filtered.Add((_rows[i], i));
        }
        var filteredTotal = filtered.Count;

        if (!string.IsNullOrWhiteSpace(sortColumn) && Columns.Contains(sortColumn))
        {
            var dir = CachedRowOps.Direction(sortDir);
            filtered.Sort((a, b) =>
            {
                var c = CachedRowOps.Compare(
                    a.Row.GetValueOrDefault(sortColumn!), b.Row.GetValueOrDefault(sortColumn!)) * dir;
                return c != 0 ? c : a.Index.CompareTo(b.Index);
            });
        }

        var page = filtered
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(x => x.Row)
            .ToList();

        return new CachedResultPage(page, filteredTotal, _rows.Count);
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> AsRowList() => _rows;

    public void Dispose()
    {
        // Rows are plain managed objects; the GC reclaims them once the job is evicted.
    }
}
