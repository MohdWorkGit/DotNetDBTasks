using System.Globalization;

namespace DotNetDBTasks.Infrastructure.Caching;

/// <summary>
/// Shared filter and ordering rules for a cached result, used identically by the in-memory and
/// disk-backed implementations so both produce the same page for a given filter/sort. Extracted
/// from the original controller-side grid logic (per-column case-insensitive "contains" filter;
/// nulls-first, same-type comparable, numeric then string fallback ordering).
/// </summary>
internal static class CachedRowOps
{
    public static bool Matches(
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyDictionary<string, string> filters)
    {
        foreach (var (column, term) in filters)
        {
            if (string.IsNullOrEmpty(term))
                continue;
            var cell = row.TryGetValue(column, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
            if (!cell.Contains(term, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Orders two cell values: nulls first; same-typed comparables compare directly; otherwise a
    /// numeric compare when both parse as numbers, and finally a case-insensitive string compare.
    /// </summary>
    public static int Compare(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        if (a.GetType() == b.GetType() && a is IComparable comparable)
            return comparable.CompareTo(b);

        if (decimal.TryParse(a.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var da) &&
            decimal.TryParse(b.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var db))
            return da.CompareTo(db);

        return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>+1 for ascending, -1 for descending ("desc", case-insensitive).</summary>
    public static int Direction(string? sortDir) =>
        string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
}
