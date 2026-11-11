using System.Globalization;
using Jadwal.Domain.Enums;

namespace Jadwal.Application.Features.Ai;

/// <summary>
/// What Jadwal asks the AI model, and the shape it must answer in. Kept in one place so the
/// wording can be tuned against the test set (docs/LOCAL-AI-OPTIONS.md §10) without touching
/// the handlers.
/// </summary>
internal static class AiPrompts
{
    /// <summary>The rules every task shares: the database, Jadwal's SQL style, and the language.</summary>
    public static string System(DatabaseServerType serverType, string task) => $"""
        You help report authors in Jadwal, a reporting tool, with SQL queries. {task}
        The database is {DatabaseName(serverType)}.
        Jadwal SQL style: parameters are written @name (the user is asked for a value when the query runs) and names may be quoted with [square brackets]. Jadwal converts both for the database when the query runs, so keep that style even if the database writes them differently. Jadwal does not allow semicolons or -- comments in a query.
        Write for a business user who knows a little SQL: short, plain sentences, no jargon.
        Write every sentence in {AnswerLanguage()}. Keep table, column and parameter names exactly as they are written in the SQL.
        Reply only with JSON that matches the schema.
        """;

    public const string ExplainTask = "Your task is to explain what a query does.";

    public static string ExplainUser(string sql) => $"""
        Explain this query.

        ```sql
        {sql}
        ```

        summary: two or three sentences on what the query returns and what it is useful for.
        steps: one short item per main part (where the data comes from, joins, filters, grouping, sorting), in the order the database applies them.
        cautions: real risks only, e.g. rows that could be missed or counted twice, a parameter that could be left empty, a filter that could be slow on a big table. An empty list if there are none.
        {LanguageReminder()}
        """;

    public const string ExplainSchema = """
        {
          "type": "object",
          "properties": {
            "summary": { "type": "string" },
            "steps": { "type": "array", "items": { "type": "string" } },
            "cautions": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["summary", "steps", "cautions"],
          "additionalProperties": false
        }
        """;

    public const string FixTask = "Your task is to fix a query that failed when it was tested.";

    public static string FixUser(string sql, string error, QueryType kind) => $"""
        This query failed when it was tested.

        ```sql
        {sql}
        ```

        The error:

        ```
        {error}
        ```

        fixedSql: the corrected query. Change only what the error needs. It must still be a single {kind.ToString().ToUpperInvariant()} statement starting with {kind.ToString().ToUpperInvariant()} (no WITH), keep every @parameter, and keep the [bracket] style.
        If the error cannot be fixed from the SQL alone (for example a table or column whose right name you cannot know), return the query unchanged.
        explanation: one or two sentences on what was wrong and what you changed; if you returned it unchanged, say what the author should check.
        {LanguageReminder()}
        """;

    public const string FixSchema = """
        {
          "type": "object",
          "properties": {
            "explanation": { "type": "string" },
            "fixedSql": { "type": "string" }
          },
          "required": ["explanation", "fixedSql"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Repeated at the end of the task, where a model weighs it most. Not enough for a very small
    /// model: qwen3:4b still explains a fix in English when the database error is in English,
    /// whatever the page's language (its Explain answers do come back in Arabic). Judge Arabic on
    /// the production model, not the development one.
    /// </summary>
    private static string LanguageReminder() =>
        $"Write the text in {AnswerLanguage()}, even if the SQL or the error is in another language.";

    /// <summary>The page's language (Accept-Language → request culture), as the model should write.</summary>
    private static string AnswerLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? "Arabic" : "English";

    private static string DatabaseName(DatabaseServerType serverType) => serverType switch
    {
        DatabaseServerType.Oracle => "Oracle",
        DatabaseServerType.SqlServer => "Microsoft SQL Server",
        DatabaseServerType.PostgreSql => "PostgreSQL",
        DatabaseServerType.MySql => "MySQL",
        _ => serverType.ToString()
    };
}
