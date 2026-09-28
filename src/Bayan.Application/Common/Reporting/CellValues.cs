using System.Globalization;

namespace Bayan.Application.Common.Reporting;

/// <summary>
/// How a result cell becomes a label or a plottable number. Shared by every chart and KPI so a
/// report chart and a dashboard tile read the same cell the same way.
/// </summary>
public static class CellValues
{
    public static string Text(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>
    /// Coerces a cell to a plottable number. Anything that is not one becomes null — a gap —
    /// rather than zero, because a category with no value and a category worth zero are
    /// different statements and a chart should not conflate them.
    /// </summary>
    public static double? Number(object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return null;
            case double d:
                return d;
            case float f:
                return f;
            case decimal m:
                return (double)m;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            case bool b:
                return b ? 1 : 0;
            case string text:
                return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            default:
                return null;
        }
    }
}
