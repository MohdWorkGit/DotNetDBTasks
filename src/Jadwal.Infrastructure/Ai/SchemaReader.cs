using System.Data.Common;
using System.Text.RegularExpressions;
using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Common.Models;
using Jadwal.Domain.Enums;
using Jadwal.Domain.Exceptions;
using Jadwal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Jadwal.Infrastructure.Ai;

/// <summary>
/// <see cref="ISchemaReader"/> over each database's own catalog, through the same executor every
/// query uses, so the connection's own account and its own rights decide what is seen.
///
/// <list type="bullet">
/// <item>Oracle: <c>ALL_OBJECTS</c>, <c>ALL_TAB_COLUMNS</c>, <c>ALL_CONSTRAINTS</c> — everything the
/// account can read, not only what it owns, because a read-only report account usually owns
/// nothing; schemas Oracle maintains itself are left out.</item>
/// <item>SQL Server, PostgreSQL, MySQL: <c>INFORMATION_SCHEMA</c>.</item>
/// </list>
///
/// <para>The catalog SQL avoids <c>[</c> and literal <c>@</c>: the executor rewrites both per
/// database. Object names reach SQL only from the catalog, never from the client.</para>
/// </summary>
public sealed partial class SchemaReader : ISchemaReader
{
    private const int CatalogTimeoutSeconds = 30;
    private const int MaxObjects = 5000;
    private const int SampleRows = 5;
    private const int SamplesPerColumn = 5;
    private const int SampleMaxLength = 40;

    private readonly IQueryExecutor _executor;
    private readonly ApplicationDbContext _context;
    private readonly IAppLocalizer _messages;

    public SchemaReader(IQueryExecutor executor, ApplicationDbContext context, IAppLocalizer messages)
    {
        _executor = executor;
        _context = context;
        _messages = messages;
    }

    public async Task<IReadOnlyList<SchemaObject>> ListObjectsAsync(SchemaTarget target, CancellationToken cancellationToken)
    {
        var rows = await RunAsync(target, ListSql(target.ServerType), new(), MaxObjects, cancellationToken);
        var hidden = target.IsDefaultConnection ? JadwalTables() : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var objects = new List<SchemaObject>();
        foreach (var row in rows)
        {
            var schema = Text(row, "OBJ_SCHEMA");
            var name = Text(row, "OBJ_NAME");
            if (name.Length == 0 || hidden.Contains(name))
                continue;
            var kind = Text(row, "OBJ_TYPE").Contains("VIEW", StringComparison.OrdinalIgnoreCase) ? "view" : "table";
            var own = string.Equals(schema, Text(row, "CUR_SCHEMA"), StringComparison.OrdinalIgnoreCase);
            // SQL Server names are always written with their schema (dbo.Orders); elsewhere the
            // connection's own schema is implied.
            var qualified = own && target.ServerType != DatabaseServerType.SqlServer
                ? Quote(name, target.ServerType)
                : $"{Quote(schema, target.ServerType)}.{Quote(name, target.ServerType)}";
            objects.Add(new SchemaObject(schema, name, kind, qualified));
        }
        return objects;
    }

    public async Task<IReadOnlyList<SchemaObjectDetail>> DescribeAsync(
        SchemaTarget target, IReadOnlyList<SchemaObject> objects, bool includeSamples, CancellationToken cancellationToken)
    {
        var picked = objects.ToDictionary(o => (o.Schema.ToUpperInvariant(), o.Name.ToUpperInvariant()), o => o);
        var details = new List<SchemaObjectDetail>();

        foreach (var obj in objects)
        {
            var parameters = new Dictionary<string, object?> { ["objOwner"] = obj.Schema, ["objName"] = obj.Name };

            var columns = (await RunAsync(target, ColumnsSql(target.ServerType), parameters, 2000, cancellationToken))
                .Select(r => new SchemaColumn(
                    Text(r, "COL_NAME"),
                    Text(r, "COL_TYPE"),
                    Text(r, "COL_NULLABLE").StartsWith("Y", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var keyRows = await RunAsync(target, KeysSql(target.ServerType), parameters, 2000, cancellationToken);
            var primaryKey = keyRows
                .Where(r => Text(r, "KEY_TYPE") is "P" or "PRIMARY KEY")
                .Select(r => Text(r, "KEY_COLUMN"))
                .ToList();
            var foreignKeys = keyRows
                .Where(r => Text(r, "KEY_TYPE") is "R" or "FOREIGN KEY")
                .GroupBy(r => Text(r, "KEY_NAME"))
                .Select(g =>
                {
                    var first = g.First();
                    // Shown as the model would write it: qualified when the target was picked too.
                    var referenced = picked.TryGetValue(
                        (Text(first, "REF_SCHEMA").ToUpperInvariant(), Text(first, "REF_TABLE").ToUpperInvariant()),
                        out var known)
                        ? known.QualifiedName
                        : Text(first, "REF_TABLE");
                    return new SchemaForeignKey(
                        g.Select(r => Text(r, "KEY_COLUMN")).ToList(),
                        referenced,
                        g.Select(r => Text(r, "REF_COLUMN")).ToList());
                })
                .ToList();

            var samples = includeSamples
                ? await SamplesAsync(target, obj, cancellationToken)
                : new Dictionary<string, IReadOnlyList<string>>();

            details.Add(new SchemaObjectDetail(obj, columns, primaryKey, foreignKeys, samples));
        }
        return details;
    }

    /// <summary>
    /// A few example values of each text column, from the first rows. Names come from the
    /// catalog and are quoted with brackets, which the executor turns into each database's own.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> SamplesAsync(
        SchemaTarget target, SchemaObject obj, CancellationToken cancellationToken)
    {
        var sql = $"SELECT * FROM [{obj.Schema}].[{obj.Name}]";
        var result = await RunResultAsync(target, sql, new(), SampleRows, cancellationToken);
        var samples = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var column in result.Columns)
        {
            var values = result.Rows
                .Select(r => r.TryGetValue(column, out var v) ? v as string : null)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Length <= SampleMaxLength ? v : v[..SampleMaxLength] + "…")
                .Distinct()
                .Take(SamplesPerColumn)
                .ToList();
            if (values.Count > 0)
                samples[column] = values;
        }
        return samples;
    }

    private async Task<List<Dictionary<string, object?>>> RunAsync(
        SchemaTarget target, string sql, Dictionary<string, object?> parameters, int maxRows, CancellationToken cancellationToken) =>
        (await RunResultAsync(target, sql, parameters, maxRows, cancellationToken)).Rows;

    private async Task<QueryExecutionResult> RunResultAsync(
        SchemaTarget target, string sql, Dictionary<string, object?> parameters, int maxRows, CancellationToken cancellationToken)
    {
        try
        {
            return await _executor.ExecuteAsync(
                sql, parameters, CatalogTimeoutSeconds, target.ConnectionString, target.ServerType, maxRows, cancellationToken);
        }
        catch (DbException ex)
        {
            throw new DomainException(_messages[MessageKeys.AiSchemaFailed, ex.Message]);
        }
    }

    /// <summary>Jadwal's own tables, never shown from the default connection: they are the app, not data.</summary>
    private HashSet<string> JadwalTables()
    {
        var names = _context.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Append("__EFMigrationsHistory");
        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A name as the model should write it: bare when it is an ordinary identifier, in Jadwal's
    /// [brackets] otherwise — Oracle's ordinary identifiers are upper case; a mixed-case one was
    /// created quoted and must be written quoted.
    /// </summary>
    private static string Quote(string name, DatabaseServerType serverType)
    {
        var plain = serverType == DatabaseServerType.Oracle ? OraclePlainName() : PlainName();
        return plain.IsMatch(name) ? name : $"[{name}]";
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_$#]*$")]
    private static partial Regex OraclePlainName();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainName();

    /// <summary>A cell as text, whatever case the database gave the column name (PostgreSQL folds to lower).</summary>
    private static string Text(Dictionary<string, object?> row, string column)
    {
        foreach (var (key, value) in row)
        {
            if (string.Equals(key, column, StringComparison.OrdinalIgnoreCase))
                return value?.ToString()?.Trim() ?? string.Empty;
        }
        return string.Empty;
    }

    // ---------------------------------------------------------------- catalog SQL

    private static string ListSql(DatabaseServerType serverType) => serverType switch
    {
        DatabaseServerType.Oracle => """
            SELECT o.OWNER AS OBJ_SCHEMA, o.OBJECT_NAME AS OBJ_NAME, o.OBJECT_TYPE AS OBJ_TYPE,
                   SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AS CUR_SCHEMA
              FROM ALL_OBJECTS o
             WHERE o.OBJECT_TYPE IN ('TABLE', 'VIEW')
               AND o.OBJECT_NAME NOT LIKE 'BIN$%'
               AND o.OWNER NOT IN (SELECT u.USERNAME FROM ALL_USERS u WHERE u.ORACLE_MAINTAINED = 'Y')
             ORDER BY CASE WHEN o.OWNER = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') THEN 0 ELSE 1 END,
                      o.OWNER, o.OBJECT_NAME
            """,
        DatabaseServerType.SqlServer => """
            SELECT TABLE_SCHEMA AS OBJ_SCHEMA, TABLE_NAME AS OBJ_NAME, TABLE_TYPE AS OBJ_TYPE, SCHEMA_NAME() AS CUR_SCHEMA
              FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
             ORDER BY CASE WHEN TABLE_SCHEMA = SCHEMA_NAME() THEN 0 ELSE 1 END, TABLE_SCHEMA, TABLE_NAME
            """,
        DatabaseServerType.PostgreSql => """
            SELECT table_schema AS obj_schema, table_name AS obj_name, table_type AS obj_type, current_schema() AS cur_schema
              FROM information_schema.tables
             WHERE table_schema NOT IN ('pg_catalog', 'information_schema') AND table_schema NOT LIKE 'pg_toast%'
             ORDER BY CASE WHEN table_schema = current_schema() THEN 0 ELSE 1 END, table_schema, table_name
            """,
        DatabaseServerType.MySql => """
            SELECT TABLE_SCHEMA AS OBJ_SCHEMA, TABLE_NAME AS OBJ_NAME, TABLE_TYPE AS OBJ_TYPE, DATABASE() AS CUR_SCHEMA
              FROM INFORMATION_SCHEMA.TABLES
             WHERE TABLE_SCHEMA = DATABASE()
             ORDER BY TABLE_NAME
            """,
        _ => throw new NotSupportedException($"No catalog query for {serverType}.")
    };

    private static string ColumnsSql(DatabaseServerType serverType) => serverType switch
    {
        DatabaseServerType.Oracle => """
            SELECT COLUMN_NAME AS COL_NAME, DATA_TYPE AS COL_TYPE, NULLABLE AS COL_NULLABLE
              FROM ALL_TAB_COLUMNS
             WHERE OWNER = @objOwner AND TABLE_NAME = @objName
             ORDER BY COLUMN_ID
            """,
        _ => """
            SELECT COLUMN_NAME AS COL_NAME, DATA_TYPE AS COL_TYPE, IS_NULLABLE AS COL_NULLABLE
              FROM INFORMATION_SCHEMA.COLUMNS
             WHERE TABLE_SCHEMA = @objOwner AND TABLE_NAME = @objName
             ORDER BY ORDINAL_POSITION
            """
    };

    private static string KeysSql(DatabaseServerType serverType) => serverType switch
    {
        DatabaseServerType.Oracle => """
            SELECT c.CONSTRAINT_TYPE AS KEY_TYPE, c.CONSTRAINT_NAME AS KEY_NAME, cc.COLUMN_NAME AS KEY_COLUMN,
                   rc.OWNER AS REF_SCHEMA, rc.TABLE_NAME AS REF_TABLE, rcc.COLUMN_NAME AS REF_COLUMN
              FROM ALL_CONSTRAINTS c
              JOIN ALL_CONS_COLUMNS cc ON cc.OWNER = c.OWNER AND cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
              LEFT JOIN ALL_CONSTRAINTS rc ON rc.OWNER = c.R_OWNER AND rc.CONSTRAINT_NAME = c.R_CONSTRAINT_NAME
              LEFT JOIN ALL_CONS_COLUMNS rcc ON rcc.OWNER = rc.OWNER AND rcc.CONSTRAINT_NAME = rc.CONSTRAINT_NAME
                                            AND rcc.POSITION = cc.POSITION
             WHERE c.OWNER = @objOwner AND c.TABLE_NAME = @objName AND c.CONSTRAINT_TYPE IN ('P', 'R')
             ORDER BY c.CONSTRAINT_TYPE, c.CONSTRAINT_NAME, cc.POSITION
            """,
        DatabaseServerType.MySql => """
            SELECT tc.CONSTRAINT_TYPE AS KEY_TYPE, tc.CONSTRAINT_NAME AS KEY_NAME, kcu.COLUMN_NAME AS KEY_COLUMN,
                   kcu.REFERENCED_TABLE_SCHEMA AS REF_SCHEMA, kcu.REFERENCED_TABLE_NAME AS REF_TABLE,
                   kcu.REFERENCED_COLUMN_NAME AS REF_COLUMN
              FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
              JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                ON kcu.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA AND kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
               AND kcu.TABLE_NAME = tc.TABLE_NAME
             WHERE tc.TABLE_SCHEMA = @objOwner AND tc.TABLE_NAME = @objName
               AND tc.CONSTRAINT_TYPE IN ('PRIMARY KEY', 'FOREIGN KEY')
             ORDER BY tc.CONSTRAINT_TYPE, tc.CONSTRAINT_NAME, kcu.ORDINAL_POSITION
            """,
        // SQL Server and PostgreSQL: the referenced columns come through the unique constraint
        // the foreign key points at, matched by position.
        _ => $"""
            SELECT tc.CONSTRAINT_TYPE AS KEY_TYPE, tc.CONSTRAINT_NAME AS KEY_NAME, kcu.COLUMN_NAME AS KEY_COLUMN,
                   ukcu.TABLE_SCHEMA AS REF_SCHEMA, ukcu.TABLE_NAME AS REF_TABLE, ukcu.COLUMN_NAME AS REF_COLUMN
              FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
              JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                ON kcu.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA AND kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
               AND kcu.TABLE_NAME = tc.TABLE_NAME
              LEFT JOIN INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                ON rc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA AND rc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
              LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ukcu
                ON ukcu.CONSTRAINT_SCHEMA = rc.UNIQUE_CONSTRAINT_SCHEMA AND ukcu.CONSTRAINT_NAME = rc.UNIQUE_CONSTRAINT_NAME
               AND ukcu.ORDINAL_POSITION = {(serverType == DatabaseServerType.PostgreSql ? "kcu.POSITION_IN_UNIQUE_CONSTRAINT" : "kcu.ORDINAL_POSITION")}
             WHERE tc.TABLE_SCHEMA = @objOwner AND tc.TABLE_NAME = @objName
               AND tc.CONSTRAINT_TYPE IN ('PRIMARY KEY', 'FOREIGN KEY')
             ORDER BY tc.CONSTRAINT_TYPE, tc.CONSTRAINT_NAME, kcu.ORDINAL_POSITION
            """
    };
}
