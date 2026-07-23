using DotNetDBTasks.Application.Common.Models;

namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// A cached read result held on behalf of a finished query job. Small results keep their rows
/// in the managed heap; large results are spilled to local disk and only their metadata lives
/// in memory. Consumers page/export through this abstraction and never touch the underlying
/// storage directly, so the memory-vs-disk decision is invisible to callers.
/// </summary>
public interface ICachedResult : IDisposable
{
    IReadOnlyList<string> Columns { get; }

    /// <summary>Total rows in the result, ignoring any filter.</summary>
    int TotalRows { get; }

    /// <summary>
    /// Approximate footprint used for the cache budget — heap bytes for an in-memory result,
    /// on-disk bytes for a spilled one. Interpreted together with <see cref="IsOnDisk"/>.
    /// </summary>
    long ApproxSizeBytes { get; }

    /// <summary>True when the rows are spilled to disk rather than held in the managed heap.</summary>
    bool IsOnDisk { get; }

    /// <summary>
    /// Returns one page after applying case-insensitive "contains" column filters and an optional
    /// sort. An unknown <paramref name="sortColumn"/> is ignored (natural order). Mirrors the
    /// server-side grid semantics: nulls sort first, same-typed comparables compare directly,
    /// otherwise a numeric then case-insensitive string fallback is used.
    /// </summary>
    CachedResultPage GetPage(
        int pageIndex,
        int pageSize,
        string? sortColumn,
        string? sortDir,
        IReadOnlyDictionary<string, string> filters);

    /// <summary>
    /// Exposes the rows as a list for the file exporters. For a spilled result this is a lazy,
    /// re-enumerable view that streams from disk (Count is O(1)); nothing is re-materialized into
    /// the heap. For an in-memory result it is the backing list.
    /// </summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> AsRowList();
}
