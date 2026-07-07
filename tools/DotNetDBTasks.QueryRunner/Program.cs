using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace DotNetDBTasks.QueryRunner;

/// <summary>
/// Minimal headless query exporter for Windows Task Scheduler: runs the configured
/// queries in order and writes all result sets into one Excel/CSV/JSON file. Queries
/// with a KeyColumn are incremental — the run starts where the previous one stopped,
/// via a checkpoint kept in a small state file next to the config.
/// Usage: DotNetDBTasks.QueryRunner.exe [path\to\config.json]
/// Exit code 0 = success, 1 = failure (so the scheduler can flag failed runs).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Default to the config next to the exe, not the working directory —
        // Task Scheduler starts processes with cwd C:\Windows\System32.
        var configPath = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        RunnerConfig? config = null;
        try
        {
            config = RunnerConfig.Load(configPath);
            var state = StateStore.Load(config.StateFilePath);

            // Run every query first; the file is written and checkpoints advance only
            // when all of them succeed, so a partial failure never loses rows.
            var results = new List<QueryResult>();
            var newKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using (var connection = CreateConnection(config.Database))
            {
                await connection.OpenAsync();
                foreach (var query in config.Queries)
                {
                    var result = await ExecuteQueryAsync(connection, query, state);
                    results.Add(result);

                    var nextKey = ExtractNextKey(query, result);
                    if (nextKey is not null)
                        newKeys[query.Name] = nextKey;
                }
            }

            var format = ParseFormat(config.Output.Format);
            Directory.CreateDirectory(config.Output.Folder);
            if (config.Output.ArchiveFolder is not null)
                Directory.CreateDirectory(config.Output.ArchiveFolder);

            var fileName = config.Output.FileName
                + (config.Output.AppendTimestamp ? DateTime.Now.ToString("_yyyyMMdd-HHmmss") : string.Empty)
                + "." + FileExporter.GetExtension(format);
            var filePath = Path.Combine(config.Output.Folder, fileName);

            var destinations = new List<string> { filePath };
            if (config.Output.ArchiveFolder is not null)
                destinations.Add(Path.Combine(config.Output.ArchiveFolder, fileName));

            var primaryAppended = config.Output.AppendToExisting && File.Exists(filePath);

            foreach (var destination in destinations)
            {
                // Appending is decided per destination: a brand-new archive copy still
                // gets its own header row and BOM even while the main file is appended.
                var appending = config.Output.AppendToExisting && File.Exists(destination);
                var bytes = FileExporter.Export(
                    format,
                    results,
                    config.Output.FileName,
                    // An appended chunk must never repeat the header (or the BOM).
                    includeHeaders: config.Output.IncludeHeaders && !appending,
                    emitBom: !appending,
                    separator: config.Output.SeparatorText);

                if (appending)
                    await AppendBytesAsync(destination, bytes);
                else
                    await File.WriteAllBytesAsync(destination, bytes);
            }

            // Export succeeded — persist where each incremental query stopped.
            if (newKeys.Count > 0)
            {
                foreach (var (name, key) in newKeys)
                    state[name] = key;
                StateStore.Save(config.StateFilePath, state);
            }

            var perQuery = string.Join(", ", results.Select(r => $"{r.QueryName}={r.Rows.Count}"));
            Log(config, $"OK    | {results.Sum(r => r.Rows.Count)} row(s) ({perQuery}) -> {filePath}"
                + (primaryAppended ? " (appended)" : string.Empty)
                + (config.Output.ArchiveFolder is not null ? $" (+ copy in {config.Output.ArchiveFolder})" : string.Empty));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Log(config, $"ERROR | {ex.Message}", toConsole: false);
            return 1;
        }
    }

    private static async Task<QueryResult> ExecuteQueryAsync(
        DbConnection connection,
        QueryConfig query,
        Dictionary<string, string> state)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = query.Sql!;
        command.CommandTimeout = Math.Max(1, query.TimeoutSeconds);
        if (command is OracleCommand oracle)
            oracle.BindByName = true;

        foreach (var (name, value) in query.Parameters)
            AddParameter(command, name, string.IsNullOrEmpty(value) ? DBNull.Value : value);

        // Incremental: bind the saved checkpoint (or the configured initial key) so the
        // query selects only rows after where the previous run stopped.
        if (!string.IsNullOrWhiteSpace(query.KeyColumn))
        {
            var lastKey = state.TryGetValue(query.Name, out var saved) ? saved : query.InitialKey;
            AddParameter(command, query.KeyParameter, ConvertKey(query, lastKey));
        }

        await using var reader = await command.ExecuteReaderAsync();

        // Duplicate column names get a positional suffix so no value is silently dropped.
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            columns.Add(seen.Add(name) ? name : $"{name}_{i + 1}");
        }

        if (!string.IsNullOrWhiteSpace(query.KeyColumn)
            && !columns.Contains(query.KeyColumn, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Query '{query.Name}': KeyColumn '{query.KeyColumn}' is not in the result set. Select it explicitly.");
        }

        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
                row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return new QueryResult(query.Name, columns, rows);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// The new checkpoint is the KeyColumn value of the LAST returned row (the query
    /// must order by the key ascending). No rows → null, keeping the previous key.
    /// </summary>
    private static string? ExtractNextKey(QueryConfig query, QueryResult result)
    {
        if (string.IsNullOrWhiteSpace(query.KeyColumn) || result.Rows.Count == 0)
            return null;

        result.Rows[^1].TryGetValue(query.KeyColumn, out var value);
        return value is null ? null : FileExporter.FormatValue(value);
    }

    /// <summary>Binds the stored (string) key with the configured database type.</summary>
    private static object ConvertKey(QueryConfig query, string key) =>
        query.KeyType.Trim().ToLowerInvariant() switch
        {
            "number" => decimal.Parse(key, CultureInfo.InvariantCulture),
            "date" or "datetime" => DateTime.Parse(key, CultureInfo.InvariantCulture),
            _ => key
        };

    private static async Task AppendBytesAsync(string filePath, byte[] bytes)
    {
        await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes);
    }

    private static DbConnection CreateConnection(DatabaseConfig database) =>
        database.Provider.Trim().ToLowerInvariant() switch
        {
            "oracle" => new OracleConnection(database.ConnectionString),
            "sqlserver" => new SqlConnection(database.ConnectionString),
            "postgresql" => new NpgsqlConnection(database.ConnectionString),
            "mysql" => new MySqlConnection(database.ConnectionString),
            _ => throw new InvalidOperationException(
                $"Unknown Database.Provider '{database.Provider}'. Use Oracle, SqlServer, PostgreSql, or MySql.")
        };

    private static ExportFormat ParseFormat(string format) =>
        Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Unknown Output.Format '{format}'. Use Excel, Csv, or Json.");

    private static void Log(RunnerConfig? config, string message, bool toConsole = true)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
        if (toConsole)
            Console.WriteLine(line);

        var logFile = config?.Output.LogFile;
        if (string.IsNullOrWhiteSpace(logFile))
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            File.AppendAllText(logFile, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never turn a successful export into a failed run.
        }
    }
}
