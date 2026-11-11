using System.Text.RegularExpressions;

namespace Jadwal.Domain.Enums;

/// <summary>
/// The kind of statement a dynamic query runs, derived from its main SQL keyword.
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
    /// <summary>String literals, quoted names, bracket and backtick names, and comments.</summary>
    private static readonly Regex LiteralsAndComments = new(
        @"'(?:[^']|'')*'|""(?:[^""]|"""")*""|\[[^\]]*\]|`[^`]*`|/\*.*?\*/|--[^\r\n]*",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DataChangingWord = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The SQL with literals, quoted names and comments blanked out, so keywords can be looked
    /// for without a filter on <c>'DELETED'</c> or a column called <c>[Update Date]</c> counting.
    /// </summary>
    public static string StripLiteralsAndComments(string sql) => LiteralsAndComments.Replace(sql, " ");

    /// <summary>
    /// Classifies a statement by its main keyword, case-insensitively. Literals and comments are
    /// ignored, so a leading comment no longer hides a SELECT.
    ///
    /// <para>A <c>WITH</c> query is classified by the statement after its common table
    /// expressions: <c>WITH … SELECT</c> is a Select and can be tested, while
    /// <c>WITH … DELETE</c> — legal on SQL Server, PostgreSQL and MySQL — is a Delete and goes
    /// through the preview/confirm handshake like any other write. A SELECT whose CTEs change
    /// data (PostgreSQL's <c>WITH d AS (DELETE … RETURNING *) SELECT …</c>) stays Other: it is
    /// neither a plain read nor a write the preview can show.</para>
    /// </summary>
    public static QueryType FromSql(string? sql)
    {
        var text = StripLiteralsAndComments(sql ?? string.Empty);
        var first = WordAt(text, 0, out var end);
        if (first != "WITH")
            return KindOf(first);

        var kind = KindOf(MainKeywordAfterCtes(text, end));
        return kind == QueryType.Select && DataChangingWord.IsMatch(text) ? QueryType.Other : kind;
    }

    /// <summary>
    /// True for statements that modify data (INSERT/UPDATE/DELETE) — the ones that go through
    /// the preview/confirm handshake and are rejected by the export path.
    /// </summary>
    public static bool IsWrite(this QueryType type) =>
        type is QueryType.Insert or QueryType.Update or QueryType.Delete;

    private static QueryType KindOf(string keyword) => keyword switch
    {
        "SELECT" => QueryType.Select,
        "INSERT" => QueryType.Insert,
        "UPDATE" => QueryType.Update,
        "DELETE" => QueryType.Delete,
        _ => QueryType.Other
    };

    /// <summary>
    /// Steps over <c>[RECURSIVE] name [(columns)] AS [NOT] [MATERIALIZED] ( … )</c>, once per
    /// comma, and returns the keyword that follows. Quoted CTE names were blanked out already,
    /// so a name may be missing. Anything unexpected returns an empty keyword (Other).
    /// </summary>
    private static string MainKeywordAfterCtes(string text, int position)
    {
        var at = position;
        if (PeekWord(text, at) == "RECURSIVE")
            WordAt(text, at, out at);

        while (true)
        {
            var word = PeekWord(text, at);
            if (word.Length > 0 && word != "AS")
                WordAt(text, at, out at);                        // the CTE's name

            at = SkipSpace(text, at);
            if (at < text.Length && text[at] == '(')
                at = SkipBrackets(text, at);                     // its column list

            if (WordAt(text, at, out at) != "AS")
                return string.Empty;
            if (PeekWord(text, at) == "NOT")
                WordAt(text, at, out at);
            if (PeekWord(text, at) == "MATERIALIZED")
                WordAt(text, at, out at);

            at = SkipSpace(text, at);
            if (at >= text.Length || text[at] != '(')
                return string.Empty;
            at = SkipSpace(text, SkipBrackets(text, at));      // its body

            if (at < text.Length && text[at] == ',')
            {
                at++;
                continue;
            }
            return WordAt(text, at, out _);
        }
    }

    /// <summary>The upper-cased word starting at the first non-space from <paramref name="start"/>.</summary>
    private static string WordAt(string text, int start, out int end)
    {
        var at = SkipSpace(text, start);
        end = at;
        while (end < text.Length && (char.IsAsciiLetter(text[end]) || text[end] == '_'))
            end++;
        return text[at..end].ToUpperInvariant();
    }

    private static string PeekWord(string text, int start) => WordAt(text, start, out _);

    private static int SkipSpace(string text, int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
            at++;
        return at;
    }

    /// <summary>The position just past the bracket that closes the one at <paramref name="open"/>.</summary>
    private static int SkipBrackets(string text, int open)
    {
        var depth = 0;
        for (var at = open; at < text.Length; at++)
        {
            if (text[at] == '(') depth++;
            else if (text[at] == ')' && --depth == 0) return at + 1;
        }
        return text.Length;
    }
}
