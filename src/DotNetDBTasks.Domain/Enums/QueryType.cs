namespace DotNetDBTasks.Domain.Enums;

/// <summary>
/// The kind of statement a dynamic query runs, derived from its leading SQL keyword.
/// Stored on the query so it can be filtered in the database rather than re-parsed per use.
/// </summary>
public enum QueryType
{
    Select = 0,
    Insert = 1,
    Update = 2,
    Delete = 3,
    /// <summary>
    /// Anything that is not one of the four above — MERGE, DDL, an anonymous PL/SQL block.
    /// Deliberately *not* treated as a write: the execution path has always let these fall
    /// through as reads, and classifying them as writes would newly subject them to the
    /// preview/confirm handshake.
    /// </summary>
    Other = 4
}

/// <summary>
/// The single place that decides a query's type. Lives in Domain so the Application and
/// Infrastructure layers can share one implementation instead of each keeping its own copy.
/// </summary>
public static class QueryTypeClassifier
{
    /// <summary>
    /// Classifies a statement by its leading keyword. Matches the historic behaviour exactly:
    /// leading whitespace is ignored and the comparison is case-insensitive.
    /// </summary>
    public static QueryType FromSql(string? sql)
    {
        var trimmed = sql?.TrimStart() ?? string.Empty;

        if (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) return QueryType.Select;
        if (trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)) return QueryType.Insert;
        if (trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)) return QueryType.Update;
        if (trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)) return QueryType.Delete;

        return QueryType.Other;
    }

    /// <summary>
    /// True for statements that modify data (INSERT/UPDATE/DELETE) — the ones that go through
    /// the preview/confirm handshake and are rejected by the export path.
    /// </summary>
    public static bool IsWrite(this QueryType type) =>
        type is QueryType.Insert or QueryType.Update or QueryType.Delete;
}
