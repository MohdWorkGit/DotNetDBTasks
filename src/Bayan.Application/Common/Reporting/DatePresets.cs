using System.Globalization;
using System.Text.RegularExpressions;

namespace Bayan.Application.Common.Reporting;

/// <summary>
/// Relative dates a date filter can hold instead of a fixed one — <c>month_start</c>,
/// <c>today-7</c> — so a dashboard left on a wall screen, a bookmarked link or a nightly snapshot
/// keeps meaning "this month" as the months go by.
///
/// <para>The token is what is stored and what travels in the URL; it is resolved to a date only at
/// the moment a query runs, in the server's local time. The client mirrors this grammar in
/// <c>date-presets.ts</c> to show the date a token currently means.</para>
/// </summary>
public static partial class DatePresets
{
    /// <summary>Every fixed token, for validation messages and the builder hint.</summary>
    public static readonly IReadOnlyList<string> Tokens = new[]
    {
        "today", "week_start", "month_start", "month_end",
        "last_month_start", "last_month_end", "year_start"
    };

    /// <summary>
    /// Resolves a token against <paramref name="today"/>. Returns false for anything that is not a
    /// token — including an ordinary date, which the caller passes through unchanged.
    /// </summary>
    public static bool TryResolve(string? value, DateTime today, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var token = value.Trim().ToLowerInvariant();
        today = today.Date;

        var offset = OffsetRegex().Match(token);
        if (offset.Success)
        {
            var days = int.Parse(offset.Groups[2].Value, CultureInfo.InvariantCulture);
            if (days > 36_600)
                return false;   // a century; anything more is a typo, not a date
            date = today.AddDays(offset.Groups[1].Value == "-" ? -days : days);
            return true;
        }

        var monthStart = new DateTime(today.Year, today.Month, 1);
        DateTime? resolved = token switch
        {
            "today" => today,
            // Weeks start on Sunday here, as the scheduled-task day-of-week numbering already does.
            "week_start" => today.AddDays(-(int)today.DayOfWeek),
            "month_start" => monthStart,
            "month_end" => monthStart.AddMonths(1).AddDays(-1),
            "last_month_start" => monthStart.AddMonths(-1),
            "last_month_end" => monthStart.AddDays(-1),
            "year_start" => new DateTime(today.Year, 1, 1),
            _ => null
        };

        if (resolved is null)
            return false;

        date = resolved.Value;
        return true;
    }

    /// <summary>
    /// A token becomes its date as yyyy-MM-dd; anything else is returned as it came, for the
    /// query's own parameter typing to accept or reject.
    /// </summary>
    public static string Resolve(string value, DateTime today) =>
        TryResolve(value, today, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value;

    /// <summary>True for a token or a parseable date — what a date filter's default may hold.</summary>
    public static bool IsValidDateValue(string value) =>
        TryResolve(value, DateTime.Today, out _)
        || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    [GeneratedRegex(@"^today([+-])(\d{1,5})$")]
    private static partial Regex OffsetRegex();
}
