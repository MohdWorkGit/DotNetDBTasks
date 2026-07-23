using System.Globalization;
using System.Text.Json;

namespace DotNetDBTasks.Infrastructure.Caching;

/// <summary>
/// Encodes/decodes a result row as a single NDJSON line for the disk-backed cache. Each row is a
/// JSON array of cell tokens in column order; every non-null cell is a JSON string prefixed with a
/// one-char type tag so the CLR value round-trips faithfully (a real string is tagged too, so the
/// tag is never ambiguous). Preserving the type keeps disk-backed sorting, filtering, display and
/// export byte-for-byte consistent with the in-memory path.
/// </summary>
internal static class ResultRowSerializer
{
    // Tags: S string · I integral · F floating · M decimal · B bool · D DateTime · X byte[] (hex).
    public static string EncodeRow(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string> columns)
    {
        var tokens = new string?[columns.Count];
        for (int c = 0; c < columns.Count; c++)
        {
            row.TryGetValue(columns[c], out var value);
            tokens[c] = EncodeValue(value);
        }
        return JsonSerializer.Serialize(tokens);
    }

    public static Dictionary<string, object?> DecodeRow(string line, IReadOnlyList<string> columns)
    {
        var tokens = JsonSerializer.Deserialize<string?[]>(line) ?? Array.Empty<string?>();
        var row = new Dictionary<string, object?>(columns.Count);
        for (int c = 0; c < columns.Count; c++)
            row[columns[c]] = c < tokens.Length ? DecodeValue(tokens[c]) : null;
        return row;
    }

    private static string? EncodeValue(object? value) => value switch
    {
        null or DBNull => null,
        string s => "S" + s,
        bool b => b ? "B1" : "B0",
        byte or sbyte or short or ushort or int or uint or long or ulong =>
            "I" + Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        float or double =>
            "F" + Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
        decimal m => "M" + m.ToString(CultureInfo.InvariantCulture),
        DateTime dt => "D" + dt.ToString("o", CultureInfo.InvariantCulture),
        byte[] bytes => "X" + Convert.ToHexString(bytes),
        _ => "S" + (value.ToString() ?? string.Empty)
    };

    private static object? DecodeValue(string? token)
    {
        if (token is null || token.Length == 0)
            return null;

        var tag = token[0];
        var payload = token.Substring(1);
        return tag switch
        {
            'S' => payload,
            'B' => payload == "1",
            'I' => long.Parse(payload, CultureInfo.InvariantCulture),
            'F' => double.Parse(payload, CultureInfo.InvariantCulture),
            'M' => decimal.Parse(payload, CultureInfo.InvariantCulture),
            'D' => DateTime.ParseExact(payload, "o", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            'X' => Convert.FromHexString(payload),
            _ => payload
        };
    }
}
