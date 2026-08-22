using System.Globalization;
using Bayan.Application.Common.Models;
using Bayan.Domain.Enums;

namespace Bayan.Application.Common.Reporting;

/// <summary>
/// Combines two already-executed datasets into one flat table, matched on a key column.
///
/// <para>This is the other way two datasets can relate, and it is worth being clear how it
/// differs from master/detail: a <b>join</b> produces a single table where each matched pair is
/// one row, with the left side's values repeated on every match. A <b>detail</b> dataset instead
/// produces a repeating section per parent row. "Customer details in a box, then their
/// purchases" is a detail; "one row per purchase, with the customer's name on it" is a join.</para>
///
/// <para>Evaluated in memory rather than pushed into SQL because the two sides may come from
/// different saved queries, and those queries may even run against different database
/// connections. Building the join here is the only place that can see both.</para>
/// </summary>
public static class ReportJoinEvaluator
{
    /// <summary>
    /// Hash-joins <paramref name="left"/> to <paramref name="right"/>.
    ///
    /// <para>The right side is indexed once and the left is streamed against it, so the cost is
    /// proportional to the two inputs rather than to their product.</para>
    /// </summary>
    /// <param name="rightKeyPrefix">
    /// Prefix applied to right-hand columns whose names collide with the left. Without it a
    /// join of two datasets that both return <c>NAME</c> would silently lose one of them.
    /// </param>
    public static (List<string> Columns, List<Dictionary<string, object?>> Rows) Join(
        IReadOnlyList<string> leftColumns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> left,
        IReadOnlyList<string> rightColumns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> right,
        string leftKeyColumn,
        string rightKeyColumn,
        ReportJoinType joinType,
        string rightKeyPrefix)
    {
        // Right-hand column names as they will appear in the output.
        var rightNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in rightColumns)
        {
            rightNames[column] = leftColumns.Contains(column, StringComparer.OrdinalIgnoreCase)
                ? $"{rightKeyPrefix}_{column}"
                : column;
        }

        var columns = new List<string>(leftColumns);
        columns.AddRange(rightColumns.Select(c => rightNames[c]));

        // Index the right side once, by normalised key.
        var index = new Dictionary<string, List<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in right)
        {
            var key = NormalizeKey(row.TryGetValue(rightKeyColumn, out var value) ? value : null);
            if (key is null)
                continue; // a null key matches nothing, in either direction

            if (!index.TryGetValue(key, out var bucket))
                index[key] = bucket = new List<IReadOnlyDictionary<string, object?>>();
            bucket.Add(row);
        }

        var rows = new List<Dictionary<string, object?>>();

        foreach (var leftRow in left)
        {
            var key = NormalizeKey(leftRow.TryGetValue(leftKeyColumn, out var value) ? value : null);
            List<IReadOnlyDictionary<string, object?>>? matches = null;
            if (key is not null)
                index.TryGetValue(key, out matches);

            if (matches is { Count: > 0 })
            {
                foreach (var rightRow in matches)
                    rows.Add(Combine(leftColumns, leftRow, rightColumns, rightRow, rightNames));
            }
            else if (joinType == ReportJoinType.Left)
            {
                // Keep the left row, with the right-hand columns empty.
                rows.Add(Combine(leftColumns, leftRow, rightColumns, null, rightNames));
            }
        }

        return (columns, rows);
    }

    private static Dictionary<string, object?> Combine(
        IReadOnlyList<string> leftColumns,
        IReadOnlyDictionary<string, object?> leftRow,
        IReadOnlyList<string> rightColumns,
        IReadOnlyDictionary<string, object?>? rightRow,
        IReadOnlyDictionary<string, string> rightNames)
    {
        var combined = new Dictionary<string, object?>(leftColumns.Count + rightColumns.Count);

        foreach (var column in leftColumns)
            combined[column] = leftRow.TryGetValue(column, out var value) ? value : null;

        foreach (var column in rightColumns)
        {
            combined[rightNames[column]] =
                rightRow is not null && rightRow.TryGetValue(column, out var value) ? value : null;
        }

        return combined;
    }

    /// <summary>
    /// Reduces a key value to a form two sides can be compared by.
    ///
    /// <para>Necessary because the sides can come from different drivers, or even different
    /// database engines: the same customer code may arrive as <c>string</c> from one and the
    /// same account number as <c>decimal</c> from another, and <c>1</c> must match <c>1.0</c>.
    /// Everything is reduced to invariant text, numbers through a canonical numeric form, and
    /// compared case-insensitively — the same rules a reader applies when they look at two
    /// printed columns and call them equal.</para>
    ///
    /// <para>Null returns null, which matches nothing on either side — SQL semantics.</para>
    /// </summary>
    internal static string? NormalizeKey(object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return null;

            case string text:
                var trimmed = text.Trim();
                return trimmed.Length == 0 ? null : trimmed;

            case byte[] bytes:
                return Convert.ToHexString(bytes);

            case DateTime date:
                return date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            case bool flag:
                return flag ? "1" : "0";

            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                // A canonical numeric form, so 1, 1.0 and 1.00 are one key.
                var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return number.ToString("0.##########", CultureInfo.InvariantCulture);

            default:
                var fallback = value.ToString()?.Trim();
                return string.IsNullOrEmpty(fallback) ? null : fallback;
        }
    }

    /// <summary>
    /// Wraps the result as an <see cref="ExportResultSet"/> under the join dataset's own key, so
    /// the template addresses it exactly like any other section.
    /// </summary>
    public static ExportResultSet ToResultSet(
        string key,
        string title,
        (List<string> Columns, List<Dictionary<string, object?>> Rows) joined) =>
        new(joined.Columns,
            joined.Rows.Cast<IReadOnlyDictionary<string, object?>>().ToList(),
            key,
            title);
}
