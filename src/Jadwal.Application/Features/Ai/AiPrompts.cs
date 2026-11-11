using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
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
    /// The rules for drafting a dashboard. No database or SQL rules: the model sees each query
    /// only as the shape of its result.
    /// </summary>
    public static string DashboardSystem() => $"""
        You design dashboards in Jadwal, a reporting tool. A dashboard is a set of tiles on a 12-column grid; each tile shows one saved query's result as a figure, a chart or a table.
        Tile types:
        - kpi: one big number from valueColumn, with aggregate: sum, average, count (counts rows; no valueColumn needed), min, max, or last (the last row's value: only for a query that returns one row). Good for totals.
        - column or bar: compare a measure across categories; bar suits long category names or many categories.
        - line or area: a measure over time; the category must be a date column.
        - pie or doughnut: share of a whole, one measure, about 8 categories at most.
        - funnel: stages in order, one measure.
        - stackedColumn or stackedBar: two or more measures that add up.
        - table: the rows themselves, for lists.
        For a chart, categoryColumn is a text or date column and seriesColumns are number columns. Use only the columns listed for that tile's query. One query can feed several tiles.
        Widths: kpi 3, charts 6 (12 for a wide time series), table 12.
        Put kpi tiles first, then charts, then tables. Make 3 to 8 tiles and do not repeat the same view.
        Titles, the dashboard name and the description are for a business user: short and plain.
        Write every sentence in {AnswerLanguage()}. Keep column names exactly as listed.
        Reply only with JSON that matches the schema.
        """;

    public static string DashboardUser(string? goal, IReadOnlyList<QueryProfile> profiles)
    {
        var text = new StringBuilder();
        text.AppendLine(string.IsNullOrWhiteSpace(goal)
            ? "Goal: none given. Choose what a manager would want to see first."
            : $"Goal: {goal.Trim()}");
        text.AppendLine();
        text.AppendLine("Queries:");
        for (var i = 0; i < profiles.Count; i++)
        {
            var p = profiles[i];
            text.Append($"#{i + 1} \"{p.Query.Name}\"");
            if (!string.IsNullOrWhiteSpace(p.Query.Description))
                text.Append($": {p.Query.Description!.Trim()}");
            text.AppendLine();
            text.AppendLine($"   rows: {p.Rows}");
            text.AppendLine("   columns:");
            foreach (var c in p.Columns)
                text.AppendLine($"   - \"{c.Name}\": {c.Kind}, {c.Distinct} different values");
        }
        text.AppendLine();
        text.AppendLine("query: the number of the query the tile shows. reason: one short sentence on why this tile helps.");
        text.AppendLine("Unused fields: \"\" for a column, an empty list for seriesColumns, \"last\" for aggregate.");
        text.Append(LanguageReminder());
        return text.ToString();
    }

    /// <summary>
    /// The answer's shape. Column names are an enum of the real ones, so the model picks from
    /// them instead of inventing a near miss; which query each belongs to is checked afterwards.
    /// </summary>
    public static string DashboardSchema(IEnumerable<string> columnNames)
    {
        var columns = new JsonArray(
            columnNames.Distinct().Append(string.Empty).Select(c => (JsonNode?)c).ToArray());
        JsonObject OneOf(JsonArray values) =>
            new() { ["type"] = "string", ["enum"] = values };
        JsonArray Strings(IEnumerable<string> values) =>
            new(values.Select(v => (JsonNode?)v).ToArray());

        var tile = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["query"] = new JsonObject { ["type"] = "integer" },
                ["title"] = new JsonObject { ["type"] = "string" },
                ["visual"] = OneOf(Strings(DashboardDraftBuilder.Visuals.Keys)),
                ["categoryColumn"] = OneOf(columns.DeepClone().AsArray()),
                ["seriesColumns"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = OneOf(columns.DeepClone().AsArray())
                },
                ["valueColumn"] = OneOf(columns.DeepClone().AsArray()),
                ["aggregate"] = OneOf(Strings(DashboardDraftBuilder.Aggregates.Keys)),
                ["format"] = OneOf(Strings(DashboardDraftBuilder.Formats.Keys)),
                ["width"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["enum"] = new JsonArray(
                        DashboardDraftBuilder.Widths.Select(w => (JsonNode?)w).ToArray())
                },
                ["reason"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = Strings(new[]
            {
                "query", "title", "visual", "categoryColumn", "seriesColumns", "valueColumn",
                "aggregate", "format", "width", "reason"
            }),
            ["additionalProperties"] = false
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string" },
                ["description"] = new JsonObject { ["type"] = "string" },
                ["tiles"] = new JsonObject { ["type"] = "array", ["items"] = tile }
            },
            ["required"] = Strings(new[] { "name", "description", "tiles" }),
            ["additionalProperties"] = false
        }.ToJsonString();
    }

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
