namespace Bayan.Application.Common.Models;

/// <summary>
/// One page of a cached query result after server-side filtering and sorting.
/// <see cref="FilteredTotal"/> is the number of rows matching the active filters (the paging
/// denominator); <see cref="TotalRows"/> is the unfiltered row count of the whole result.
/// </summary>
public sealed record CachedResultPage(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int FilteredTotal,
    int TotalRows);
