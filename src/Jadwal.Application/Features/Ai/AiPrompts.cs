using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jadwal.Application.Common.Interfaces;
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

        fixedSql: the corrected query. Change only what the error needs. It must still be a single {kind.ToString().ToUpperInvariant()} statement{(kind == QueryType.Select ? " (it may start with WITH)" : $" starting with {kind.ToString().ToUpperInvariant()}")}, keep every @parameter, and keep the [bracket] style.
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

    public const string WriteTask = "Your task is to write one query from a description of what it should return.";

    /// <summary>
    /// The request and the tables picked for it: names exactly as SQL must write them, column
    /// types, keys (so joins follow real relationships) and, when allowed, example values.
    /// </summary>
    public static string WriteUser(string description, IReadOnlyList<SchemaObjectDetail> objects)
    {
        var text = new StringBuilder();
        text.AppendLine("Write a query for this request:");
        text.AppendLine(description.Trim());
        text.AppendLine();
        text.AppendLine("Tables you may use. Write each name exactly as shown here, with any schema prefix:");
        foreach (var o in objects)
        {
            text.AppendLine($"{(o.Object.Kind == "view" ? "VIEW" : "TABLE")} {o.Object.QualifiedName}");
            foreach (var c in o.Columns)
            {
                text.Append($"  - {c.Name}: {c.DataType}");
                if (o.PrimaryKey.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                    text.Append(", primary key");
                var fk = o.ForeignKeys.FirstOrDefault(k => k.Columns.Count == 1
                    && string.Equals(k.Columns[0], c.Name, StringComparison.OrdinalIgnoreCase));
                if (fk is not null)
                    text.Append($", refers to {fk.ReferencedObject}.{fk.ReferencedColumns.FirstOrDefault()}");
                if (o.Samples.TryGetValue(c.Name, out var samples))
                    text.Append($", for example {string.Join(", ", samples.Select(s => $"'{s}'"))}");
                text.AppendLine();
            }
        }
        text.AppendLine();
        text.AppendLine("Rules:");
        text.AppendLine("- One SELECT statement; it may start with WITH. No semicolon and no -- comments.");
        text.AppendLine("- Use only the tables and columns listed above, and join them on the keys shown. Apply every condition the request asks for (for example \"active\" becomes a filter on a status column).");
        text.AppendLine("- Write table and column names exactly as listed, without brackets: give each table a short alias and write columns as alias.COLUMN, for example e.DEPT_ID. Never put brackets around alias.COLUMN.");
        text.AppendLine("- A value the person running the query should choose (a date range, a status, a department) becomes an @parameter; do not write fixed dates or ids for those.");
        text.AppendLine($"- Give every output column a short, readable alias in {AnswerLanguage()}; only these aliases go in [square brackets], for example COUNT(e.EMP_ID) AS [Employees].");
        text.AppendLine();
        text.AppendLine("sql: the query. explanation: one or two sentences on what it returns. parameters: each @parameter the SQL uses, without the @, with a short display name and its type.");
        text.Append(LanguageReminder());
        return text.ToString();
    }

    public const string WriteSchema = """
        {
          "type": "object",
          "properties": {
            "sql": { "type": "string" },
            "explanation": { "type": "string" },
            "parameters": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "displayName": { "type": "string" },
                  "type": { "type": "string", "enum": ["string", "number", "date", "boolean"] }
                },
                "required": ["name", "displayName", "type"],
                "additionalProperties": false
              }
            }
          },
          "required": ["sql", "explanation", "parameters"],
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
        AppendQueries(text, profiles);
        text.AppendLine("query: the number of the query the tile shows. reason: one short sentence on why this tile helps.");
        text.AppendLine("Unused fields: \"\" for a column, an empty list for seriesColumns, \"last\" for aggregate.");
        text.Append(LanguageReminder());
        return text.ToString();
    }

    /// <summary>The picked queries by number, each as the shape of its result: names, kinds, counts.</summary>
    private static void AppendQueries(StringBuilder text, IReadOnlyList<QueryProfile> profiles)
    {
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
    }

    /// <summary>
    /// The rules for drafting a report: a document with one section per query, and charts drawn
    /// from sections that are already summaries.
    /// </summary>
    public static string ReportSystem() => $"""
        You design reports in Jadwal, a reporting tool. A report is a document with one section per saved query: the section shows the query's rows as a table, under a title. Charts are drawn from a section's rows.
        A chart draws one row per category and does not add rows up, so only chart a query that already returns a summary: few rows, one per category. A query with many rows (a list of orders, say) gets a section but no chart.
        Chart types: column or bar to compare categories (bar for long names or many categories), line or area for a measure over dates, pie or doughnut for shares of a whole (one measure, few categories), stackedColumn or stackedBar for measures that add up.
        For a chart, categoryColumn is a text or date column and seriesColumns are number columns of the same query. Use only the columns listed for that query.
        Give each section a short English key (letters, digits and underscores, starting with a letter, such as sales_by_branch) and a title. Make at most one or two charts per summary query.
        Titles, the report name and the description are for a business user: short and plain.
        Write every sentence in {AnswerLanguage()}. Keep column names exactly as listed.
        Reply only with JSON that matches the schema.
        """;

    public static string ReportUser(string? goal, IReadOnlyList<QueryProfile> profiles)
    {
        var text = new StringBuilder();
        text.AppendLine(string.IsNullOrWhiteSpace(goal)
            ? "Goal: none given. Make the report a manager would read first."
            : $"Goal: {goal.Trim()}");
        AppendQueries(text, profiles);
        text.AppendLine("sections: one per query, with query (its number), key and title.");
        text.AppendLine("charts: query (its number), title, type, categoryColumn, seriesColumns, and reason: one short sentence on why the chart helps. An empty list if no query is a summary.");
        text.Append(LanguageReminder());
        return text.ToString();
    }

    /// <summary>The report answer's shape, column names held to the real ones as for dashboards.</summary>
    public static string ReportSchema(IEnumerable<string> columnNames)
    {
        var columns = new JsonArray(
            columnNames.Distinct().Append(string.Empty).Select(c => (JsonNode?)c).ToArray());
        JsonObject OneOf(JsonArray values) => new() { ["type"] = "string", ["enum"] = values };
        JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)v).ToArray());
        JsonObject Text() => new() { ["type"] = "string" };

        var section = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["query"] = new JsonObject { ["type"] = "integer" },
                ["key"] = Text(),
                ["title"] = Text()
            },
            ["required"] = Strings(new[] { "query", "key", "title" }),
            ["additionalProperties"] = false
        };
        var chart = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["query"] = new JsonObject { ["type"] = "integer" },
                ["title"] = Text(),
                ["type"] = OneOf(Strings(ReportDraftBuilder.ChartTypes.Keys)),
                ["categoryColumn"] = OneOf(columns.DeepClone().AsArray()),
                ["seriesColumns"] = new JsonObject { ["type"] = "array", ["items"] = OneOf(columns.DeepClone().AsArray()) },
                ["reason"] = Text()
            },
            ["required"] = Strings(new[] { "query", "title", "type", "categoryColumn", "seriesColumns", "reason" }),
            ["additionalProperties"] = false
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = Text(),
                ["description"] = Text(),
                ["sections"] = new JsonObject { ["type"] = "array", ["items"] = section },
                ["charts"] = new JsonObject { ["type"] = "array", ["items"] = chart }
            },
            ["required"] = Strings(new[] { "name", "description", "sections", "charts" }),
            ["additionalProperties"] = false
        }.ToJsonString();
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
