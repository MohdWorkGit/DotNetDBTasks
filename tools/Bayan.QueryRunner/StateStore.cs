using System.Text.Json;

namespace Bayan.QueryRunner;

/// <summary>
/// Per-job checkpoint storage: a small JSON file mapping query name → the key value
/// where that query stopped on the last successful run. Saved only after the output
/// file is written, so a failed run re-exports the same window (at-least-once).
/// </summary>
public static class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var state = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        return state is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(state, StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(string path, Dictionary<string, string> state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }
}
