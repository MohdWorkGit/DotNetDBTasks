using System.Text.Json;

namespace DotNetDBTasks.QueryRunner;

/// <summary>
/// Configuration for one run, loaded from a JSON file (default: appsettings.json next
/// to the executable; pass a different path as the first command-line argument so a
/// single installed copy can serve several Windows Task Scheduler jobs).
/// </summary>
public class RunnerConfig
{
    public DatabaseConfig Database { get; set; } = new();

    /// <summary>Legacy single-query form; merged into <see cref="Queries"/> when present.</summary>
    public QueryConfig? Query { get; set; }

    /// <summary>
    /// Queries run in list order; every result set is appended into the same output
    /// file (first query first).
    /// </summary>
    public List<QueryConfig> Queries { get; set; } = new();

    public OutputConfig Output { get; set; } = new();

    /// <summary>Directory of the config file; relative paths in the config resolve against it.</summary>
    public string ConfigDirectory { get; private set; } = string.Empty;

    /// <summary>Resolved path of the checkpoint state file for this config.</summary>
    public string StateFilePath { get; private set; } = string.Empty;

    public static RunnerConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Config file not found: {path}");

        var config = JsonSerializer.Deserialize<RunnerConfig>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip })
            ?? throw new InvalidOperationException($"Config file is empty or invalid: {path}");

        config.Validate(Path.GetFullPath(path));
        return config;
    }

    /// <summary>Validates required fields and resolves relative paths against the config directory.</summary>
    private void Validate(string configPath)
    {
        ConfigDirectory = Path.GetDirectoryName(configPath)!;

        if (string.IsNullOrWhiteSpace(Database.ConnectionString))
            throw new InvalidOperationException("Database.ConnectionString is required.");

        if (Query is not null && Queries.Count == 0)
            Queries.Add(Query);
        if (Queries.Count == 0)
            throw new InvalidOperationException("Provide at least one query in Queries (or the single Query section).");

        for (int i = 0; i < Queries.Count; i++)
        {
            var query = Queries[i];
            if (string.IsNullOrWhiteSpace(query.Name))
                query.Name = $"query{i + 1}";

            if (string.IsNullOrWhiteSpace(query.Sql) && string.IsNullOrWhiteSpace(query.SqlFile))
                throw new InvalidOperationException($"Query '{query.Name}': provide the SQL in Sql or a file path in SqlFile.");

            if (!string.IsNullOrWhiteSpace(query.SqlFile))
            {
                var sqlPath = Path.GetFullPath(query.SqlFile, ConfigDirectory);
                if (!File.Exists(sqlPath))
                    throw new InvalidOperationException($"Query '{query.Name}': SqlFile not found: {sqlPath}");
                query.Sql = File.ReadAllText(sqlPath);
            }
        }

        var duplicateName = Queries.GroupBy(q => q.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateName is not null)
            throw new InvalidOperationException(
                $"Query name '{duplicateName.Key}' is used more than once; names identify checkpoints and must be unique.");

        if (string.IsNullOrWhiteSpace(Output.Folder))
            throw new InvalidOperationException("Output.Folder is required.");
        Output.Folder = Path.GetFullPath(Output.Folder, ConfigDirectory);

        if (!string.IsNullOrWhiteSpace(Output.LogFile))
            Output.LogFile = Path.GetFullPath(Output.LogFile, ConfigDirectory);

        if (Output.AppendToExisting)
        {
            if (!Output.Format.Equals("Csv", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Output.AppendToExisting is only supported for the Csv format.");
            if (Output.AppendTimestamp)
                throw new InvalidOperationException(
                    "Output.AppendToExisting requires AppendTimestamp=false (a new timestamped file would never be appended to).");
        }

        // Checkpoints live next to the config by default, one state file per config/job.
        StateFilePath = string.IsNullOrWhiteSpace(Output.StateFile)
            ? Path.Combine(ConfigDirectory, Path.GetFileNameWithoutExtension(configPath) + ".state.json")
            : Path.GetFullPath(Output.StateFile, ConfigDirectory);
    }
}

public class DatabaseConfig
{
    /// <summary>Oracle (default), SqlServer, PostgreSql, or MySql.</summary>
    public string Provider { get; set; } = "Oracle";

    public string ConnectionString { get; set; } = string.Empty;
}

public class QueryConfig
{
    /// <summary>Identifies the query in logs and in the checkpoint state file. Defaults to query1, query2, …</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The SELECT statement to run. Use bind variables (:name for Oracle, @name otherwise).</summary>
    public string? Sql { get; set; }

    /// <summary>Alternative to Sql: path of a .sql file (relative paths resolve against the config file).</summary>
    public string? SqlFile { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Bind-variable values by name, e.g. { "status": "ACTIVE" }.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new();

    // --- Incremental checkpoint (optional) -------------------------------------------
    // When KeyColumn is set the query becomes incremental: the last saved key value is
    // bound as :KeyParameter, and after a successful export the KeyColumn value of the
    // LAST returned row is saved as the new checkpoint. Order the query by KeyColumn
    // ascending so "the last row" really is where the run stopped.

    /// <summary>Result column whose last-row value is saved as the next run's starting key.</summary>
    public string? KeyColumn { get; set; }

    /// <summary>Bind-variable name that receives the saved key (default "lastKey" → :lastKey / @lastKey).</summary>
    public string KeyParameter { get; set; } = "lastKey";

    /// <summary>How the key is bound: String (default), Number, or Date.</summary>
    public string KeyType { get; set; } = "String";

    /// <summary>Key value used on the very first run, before any checkpoint exists.</summary>
    public string InitialKey { get; set; } = "0";
}

public class OutputConfig
{
    public string Folder { get; set; } = string.Empty;

    /// <summary>Base file name without extension. Defaults to "export".</summary>
    public string FileName { get; set; } = "export";

    /// <summary>Excel, Csv, or Json.</summary>
    public string Format { get; set; } = "Csv";

    /// <summary>When false, Csv/Excel output contains data rows only (no header row). Default true.</summary>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>When true (default) a _yyyyMMdd-HHmmss suffix keeps every run's file; when false the file is overwritten.</summary>
    public bool AppendTimestamp { get; set; } = true;

    /// <summary>
    /// Csv only: when true, new rows are appended to the existing output file instead of
    /// replacing it (headers are written only when the file is first created). Combine
    /// with KeyColumn checkpoints for a rolling incremental feed file.
    /// </summary>
    public bool AppendToExisting { get; set; }

    /// <summary>Optional log file; one line is appended per run (handy under Task Scheduler).</summary>
    public string? LogFile { get; set; }

    /// <summary>
    /// Optional path of the checkpoint state file. Default: "&lt;config name&gt;.state.json"
    /// next to the config file.
    /// </summary>
    public string? StateFile { get; set; }
}
