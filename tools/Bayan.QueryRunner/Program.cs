using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace Bayan.QueryRunner;

/// <summary>
/// Minimal headless query exporter for Windows Task Scheduler: runs the configured
/// queries in order and writes all result sets into one Excel/CSV/JSON file. Queries
/// with a KeyColumn are incremental — the run starts where the previous one stopped,
/// via a checkpoint kept in a small state file next to the config.
/// Usage: Bayan.QueryRunner.exe [path\to\config.json]
/// Exit code 0 = success, 1 = failure (so the scheduler can flag failed runs).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var startedAt = DateTime.Now;

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

            // One export per output file: everything combined (default), or one file
            // per query named "<FileName>_<QueryName>" when SeparateFiles is on.
            var exports = new List<(string BaseName, IReadOnlyList<QueryResult> Results)>();
            if (config.Output.SeparateFiles)
            {
                foreach (var result in results)
                {
                    // Empty FileName: each file is named after its query alone.
                    var baseName = string.IsNullOrWhiteSpace(config.Output.FileName)
                        ? result.QueryName
                        : config.Output.FileName + config.Output.QueryNameSeparator + result.QueryName;
                    exports.Add((SanitizeFileName(baseName), new[] { result }));
                }

                var clash = exports.GroupBy(e => e.BaseName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(g => g.Count() > 1);
                if (clash is not null)
                    throw new InvalidOperationException(
                        $"Output.SeparateFiles: queries {string.Join(" and ", clash.Select(e => $"'{e.Results[0].QueryName}'"))}"
                        + $" both map to the file name '{clash.Key}'; rename one of them.");
            }
            else
            {
                exports.Add((config.Output.FileName, results));
            }

            // Every file of one run gets the same timestamp suffix.
            var suffix = (config.Output.AppendTimestamp
                    ? DateTime.Now.ToString(config.Output.TimestampFormat, CultureInfo.InvariantCulture)
                    : string.Empty)
                + "." + FileExporter.GetExtension(format);

            var writtenFiles = new List<string>();
            foreach (var (baseName, exportResults) in exports)
            {
                var fileName = baseName + suffix;
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
                        exportResults,
                        baseName,
                        // An appended chunk must never repeat the header (or the BOM).
                        includeHeaders: config.Output.IncludeHeaders && !appending,
                        emitBom: !appending,
                        separator: config.Output.SeparatorText);

                    if (appending)
                        await AppendBytesAsync(destination, bytes);
                    else
                        await File.WriteAllBytesAsync(destination, bytes);
                }

                writtenFiles.Add(filePath + (primaryAppended ? " (appended)" : string.Empty));
            }

            // Export succeeded — persist where each incremental query stopped.
            if (newKeys.Count > 0)
            {
                foreach (var (name, key) in newKeys)
                    state[name] = key;
                StateStore.Save(config.StateFilePath, state);
            }

            var perQuery = string.Join(", ", results.Select(r => $"{r.QueryName}={r.Rows.Count}"));
            Log(config, startedAt, $"OK    | {results.Sum(r => r.Rows.Count)} row(s) ({perQuery}) -> {string.Join(", ", writtenFiles)}"
                + (config.Output.ArchiveFolder is not null ? $" (+ copy in {config.Output.ArchiveFolder})" : string.Empty));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Log(config, startedAt, $"ERROR | {ex.Message}", toConsole: false);
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

        try
        {
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
        catch (Exception ex) when (IsCommandTimeout(ex))
        {
            throw new TimeoutException(
                $"Query '{query.Name}' timed out: execution exceeded TimeoutSeconds={Math.Max(1, query.TimeoutSeconds)}.", ex);
        }
    }

    /// <summary>
    /// True when the exception is how the database driver reports an expired
    /// CommandTimeout. Drivers surface it as a generic cancellation (Oracle raises
    /// ORA-01013 or a bare "task was canceled"); without this the log line reads as
    /// if someone cancelled the run instead of naming the timeout. Nothing here
    /// cancels commands by hand, so any cancellation is the driver's timeout.
    /// </summary>
    private static bool IsCommandTimeout(Exception ex) =>
        ex switch
        {
            OperationCanceledException => true,
            TimeoutException => true,
            OracleException oracleEx => oracleEx.Number == 1013,  // ORA-01013: operation cancelled (timeout)
            SqlException sqlEx => sqlEx.Number == -2,             // execution timeout expired
            MySqlException mySqlEx => mySqlEx.ErrorCode == MySqlErrorCode.CommandTimeoutExpired,
            NpgsqlException npgsqlEx => npgsqlEx.InnerException is TimeoutException,
            _ => false
        };

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

    /// <summary>Replaces characters that are invalid in a Windows file name with '_'.</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned;
    }

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

    private static void Log(RunnerConfig? config, DateTime startedAt, string message, bool toConsole = true)
    {
        var endedAt = DateTime.Now;
        var line = $"start {startedAt:yyyy-MM-dd HH:mm:ss} | end {endedAt:yyyy-MM-dd HH:mm:ss}"
            + $" | took {FormatDuration(endedAt - startedAt)} | {message}";
        if (toConsole)
            Console.WriteLine(line);

        var logFile = config?.Output.LogFile;
        if (string.IsNullOrWhiteSpace(logFile))
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            RotateLogIfNeeded(config!.Output, logFile);
            File.AppendAllText(logFile, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never turn a successful export into a failed run.
        }
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 60)
            return $"{elapsed.TotalSeconds:0.0}s";
        if (elapsed.TotalMinutes < 60)
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s";
    }

    /// <summary>
    /// Size-based rotation, checked before each append: when the log reaches
    /// LogMaxSizeKB it is renamed to "&lt;name&gt;.1" (older files shift to .2, .3, …,
    /// the one past LogMaxFiles is deleted) and a fresh log is started.
    /// </summary>
    private static void RotateLogIfNeeded(OutputConfig output, string logFile)
    {
        var maxBytes = (long)output.LogMaxSizeKB * 1024;
        if (maxBytes <= 0)
            return;

        var info = new FileInfo(logFile);
        if (!info.Exists || info.Length < maxBytes)
            return;

        if (output.LogMaxFiles < 1)
        {
            File.Delete(logFile);
            return;
        }

        var oldest = $"{logFile}.{output.LogMaxFiles}";
        if (File.Exists(oldest))
            File.Delete(oldest);
        for (int i = output.LogMaxFiles - 1; i >= 1; i--)
        {
            var source = $"{logFile}.{i}";
            if (File.Exists(source))
                File.Move(source, $"{logFile}.{i + 1}");
        }
        File.Move(logFile, $"{logFile}.1");
    }
}
