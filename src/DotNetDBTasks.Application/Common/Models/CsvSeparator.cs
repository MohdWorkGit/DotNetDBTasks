namespace DotNetDBTasks.Application.Common.Models;

/// <summary>
/// Parses the configured CSV field separator of a scheduled task item. Stored as a
/// short string (null/empty = comma). Any text up to 8 characters is allowed (e.g.
/// ";", ";;", "|,"); the names "comma", "semicolon", "pipe" and "tab"/"\t" are also
/// accepted so hand-entered values work, and "tab" is the practical way to type a tab.
/// </summary>
public static class CsvSeparator
{
    public const string Default = ",";

    public const int MaxLength = 8;

    /// <summary>
    /// True when <paramref name="value"/> denotes a usable separator (or is null/empty,
    /// meaning the default comma). Quote and line-break characters are rejected — they
    /// collide with CSV quoting itself.
    /// </summary>
    public static bool TryParse(string? value, out string separator)
    {
        separator = Default;
        if (string.IsNullOrEmpty(value))
            return true;

        var named = value.Trim().ToLowerInvariant() switch
        {
            "comma" => ",",
            "semicolon" => ";",
            "pipe" => "|",
            "tab" or "\\t" => "\t",
            _ => null
        };
        if (named is not null)
        {
            separator = named;
            return true;
        }

        if (value.Length > MaxLength || value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
            return false;

        separator = value;
        return true;
    }

    /// <summary>Lenient form for execution paths: unparsable values fall back to the comma.</summary>
    public static string Parse(string? value) => TryParse(value, out var separator) ? separator : Default;
}
