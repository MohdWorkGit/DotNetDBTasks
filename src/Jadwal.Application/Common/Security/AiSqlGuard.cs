using System.Text.RegularExpressions;
using Jadwal.Domain.Enums;

namespace Jadwal.Application.Common.Security;

/// <summary>
/// The check SQL written by the AI model must pass before an admin is even shown it.
///
/// <para>Stricter than a save, because the author of this SQL is a model that can be talked into
/// things — by a misleading error message today, by a column comment once schemas are sent. A
/// save still validates it again afterwards; this only decides whether it is worth offering.
/// The rules: the same forbidden patterns as a save (<see cref="SqlSafetyRules"/>), one
/// statement, and the same kind of statement as the one it replaces, with no keyword of another
/// kind hidden inside it. A fix to a SELECT stays a read.</para>
///
/// <para>Keywords are looked for outside string literals, quoted names and comments, so a
/// column called <c>[Update Date]</c> or a filter on <c>'DELETED'</c> is not mistaken for a
/// statement.</para>
/// </summary>
public static class AiSqlGuard
{
    private static readonly string[] AlwaysForbidden =
    {
        "MERGE", "DROP", "ALTER", "CREATE", "TRUNCATE", "RENAME", "GRANT", "REVOKE",
        "EXEC", "EXECUTE", "CALL", "BEGIN", "DECLARE", "COMMIT", "ROLLBACK"
    };

    private static readonly Dictionary<QueryType, string[]> OtherKinds = new()
    {
        [QueryType.Select] = new[] { "INSERT", "UPDATE", "DELETE" },
        [QueryType.Insert] = new[] { "UPDATE", "DELETE" },
        [QueryType.Update] = new[] { "INSERT", "DELETE" },
        [QueryType.Delete] = new[] { "INSERT", "UPDATE" }
    };

    /// <summary>
    /// What is wrong with <paramref name="suggestedSql"/> as a replacement for
    /// <paramref name="originalSql"/> — a keyword, a pattern, or "SELECT → UPDATE" — or null when
    /// it may be offered. Language-neutral on purpose: it is shown inside a localized sentence.
    /// </summary>
    public static string? FindProblem(string originalSql, string suggestedSql) =>
        FindProblem(QueryTypeClassifier.FromSql(originalSql), suggestedSql);

    /// <summary>
    /// What is wrong with <paramref name="suggestedSql"/> as a statement of kind
    /// <paramref name="required"/> — for SQL written from scratch, with nothing to compare to.
    /// </summary>
    public static string? FindProblem(QueryType required, string suggestedSql)
    {
        var forbidden = SqlSafetyRules.FindForbiddenPattern(suggestedSql);
        if (forbidden is not null)
            return forbidden.ToUpperInvariant();

        if (required == QueryType.Other)
            return QueryType.Other.ToString().ToUpperInvariant();

        var stripped = QueryTypeClassifier.StripLiteralsAndComments(suggestedSql);
        var suggestedKind = QueryTypeClassifier.FromSql(suggestedSql);
        if (suggestedKind != required)
            return $"{required.ToString().ToUpperInvariant()} → {FirstWord(stripped)}";

        // For a SELECT this also catches SELECT … FOR UPDATE, a row lock no report query needs.
        foreach (var word in AlwaysForbidden.Concat(OtherKinds[required]))
        {
            if (Regex.IsMatch(stripped, $@"\b{word}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return word;
        }

        return null;
    }

    private static string FirstWord(string sql)
    {
        var match = Regex.Match(sql, @"[A-Za-z_]+");
        return match.Success ? match.Value.ToUpperInvariant() : "?";
    }
}
