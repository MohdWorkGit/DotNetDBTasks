# DotNetDBTasks.QueryRunner

A tiny standalone console app that runs one or more SELECT queries and writes all
result sets into a single file (Excel `.xlsx`, CSV, or JSON). Everything —
connection, queries, and output — comes from a JSON config file, so it can be
scheduled with Windows Task Scheduler with no user interaction. Queries can be
incremental: a per-query checkpoint remembers where the last run stopped, and the
next run continues from there.

It is completely independent of the web application: no API, no application database,
no login, and no reference to any other project in this repository — it builds and
runs entirely on its own. Its export files use the same layout as the web app's
exports.

## Build / publish

```
dotnet publish tools\DotNetDBTasks.QueryRunner -c Release -o C:\Tools\QueryRunner
```

## Configure

Edit `appsettings.json` next to the exe (see the sample in this folder):

| Setting | Meaning |
|---|---|
| `Database.Provider` | `Oracle` (default), `SqlServer`, `PostgreSql`, or `MySql` |
| `Database.ConnectionString` | Full ADO.NET connection string |
| `Queries` | List of queries, run in order; all rows land in the same output file (first query's rows first) |
| `Queries[].Name` | Identifies the query in logs and checkpoints (defaults to `query1`, `query2`, …) |
| `Queries[].Sql` / `SqlFile` | The SELECT statement inline, or a path to a `.sql` file |
| `Queries[].Parameters` | Bind-variable values by name (`:name` in Oracle SQL, `@name` otherwise) |
| `Queries[].TimeoutSeconds` | Command timeout (default 60) |
| `Output.Folder` | Folder for the export file (created if missing) |
| `Output.FileName` | Base file name without extension |
| `Output.Format` | `Excel`, `Csv`, or `Json` |
| `Output.IncludeHeaders` | `false`: no header row in Csv/Excel output (default `true`) |
| `Output.Separator` | Csv only: field separator — any text (`";"`, `";;"`, `"|,"`, …) or `comma`, `semicolon`, `pipe`, `tab` (default comma) |
| `Output.ArchiveFolder` | Optional second folder that receives a copy of the output file (appended independently in append mode) |
| `Output.AppendTimestamp` | `true`: new `name_yyyyMMdd-HHmmss.ext` per run; `false`: fixed file name |
| `Output.AppendToExisting` | Csv only: append rows to the existing file instead of replacing it (requires `AppendTimestamp: false`) |
| `Output.LogFile` | Optional; appends one line per run (result or error) |
| `Output.StateFile` | Optional checkpoint file path (default `<config name>.state.json` next to the config) |

## Incremental queries (checkpoints)

Give a query a `KeyColumn` and it only exports rows added since the previous run:

```json
{
  "Name": "new-orders",
  "Sql": "SELECT \"Id\", \"CustomerName\" FROM \"Orders\" WHERE \"Id\" > :lastKey ORDER BY \"Id\"",
  "KeyColumn": "Id",
  "KeyParameter": "lastKey",
  "KeyType": "Number",
  "InitialKey": "0"
}
```

How it works:

- The saved checkpoint (or `InitialKey` on the very first run) is bound into the SQL
  as `:lastKey` (`KeyParameter` — use `@lastKey` for non-Oracle providers).
- After the output file is written successfully, the `KeyColumn` value of the **last
  returned row** becomes the new checkpoint — so always `ORDER BY` the key ascending,
  and select the key column in the query.
- Checkpoints are stored per query name in the state file (delete it, or the entry
  in it, to re-export from `InitialKey` again).
- If a run fails, checkpoints do not advance — the next run re-selects the same rows,
  so nothing is ever skipped.
- `KeyType` controls how the key is bound: `String` (default), `Number`, or `Date`.
- A query returning no rows keeps its previous checkpoint.

Combining `KeyColumn` + `IncludeHeaders: false` + `AppendTimestamp: false` +
`AppendToExisting: true` (as in the sample config) produces a rolling CSV feed file
that grows with only the new rows on every scheduled run.

## Run

```
DotNetDBTasks.QueryRunner.exe                     # uses appsettings.json next to the exe
DotNetDBTasks.QueryRunner.exe C:\Jobs\sales.json  # or any config file you pass
```

Exit code `0` = success, `1` = failure — Task Scheduler surfaces this as the
"Last Run Result".

Passing a config path means one installed copy can serve many scheduled jobs:
create one JSON file per job and one scheduled task per JSON file.

## Windows Task Scheduler

1. Task Scheduler → **Create Task…**
2. General: pick an account that can reach the database and write to the output
   folder; select *Run whether user is logged on or not*.
3. Triggers: add your schedule (e.g. daily 07:00).
4. Actions → New:
   - **Program/script:** `C:\Tools\QueryRunner\DotNetDBTasks.QueryRunner.exe`
   - **Arguments (optional):** `C:\Jobs\sales.json`
   - **Start in:** `C:\Tools\QueryRunner` (not required — the app finds its config
     next to the exe — but keeps any relative paths predictable)
5. Check `Output.LogFile` after the first scheduled run to confirm it worked.
