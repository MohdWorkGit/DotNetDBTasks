# DotNetDBTasks

A production-ready dynamic database query execution platform built with .NET 10, Angular 21, Oracle, and Docker.

## Architecture

**Clean Architecture + CQRS** with strict layer separation:

```
src/
├── DotNetDBTasks.Domain          # Entities, interfaces, enums, exceptions (zero dependencies)
├── DotNetDBTasks.Application     # CQRS commands/queries, DTOs, validators, mappings
├── DotNetDBTasks.Infrastructure  # EF Core, repositories, JWT, encryption, query executor
└── DotNetDBTasks.API             # Controllers, middleware, DI configuration
client/                           # Angular 21 SPA with Angular Material
docker/                           # Dockerfiles and nginx config
```

**Key patterns:** Repository, Unit of Work, MediatR (CQRS), AutoMapper, FluentValidation, Serilog.

## Features

| Admin | User |
|-------|------|
| Create/edit/delete parameterized SQL queries | View queries assigned to their roles |
| Define input parameters (string, number, date, boolean) | Fill dynamically generated forms |
| Multi-value string parameters (comma-separated, `IN` support) | Execute queries with pagination |
| Organize queries into folders | Export results to CSV |
| Assign queries to roles | View personal execution history |
| Enable/disable queries, configurable timeouts | |
| Flag slow queries as long-running (async execution) | Long-running queries run as background jobs — live elapsed timer + cancel |
| Allow or block running writes without confirmation | Skip the write preview when the query allows it |
| Turn off before-change row capture on bulk writes | |
| Filter queries and execution logs by statement type | |
| Upload a site logo for the top banner | |
| Export queries to JSON and import them back / on a new system | |
| View execution audit logs | |

## Security

- LDAP / Active Directory authentication
- JWT access + refresh token authentication
- AES-256 encryption of stored database credentials at rest
- Role-based authorization (Admin, User)
- Parameterized SQL only — no string concatenation
- SQL query validation (forbidden pattern detection: `XP_`, `SP_`, `--`, `;`, `DBMS_`, `UTL_`)
- Write queries (INSERT/UPDATE/DELETE) run preview-and-confirm: previewed in a rolled-back
  transaction before any commit (see [How Write Queries Work](#how-write-queries-work-insert--update--delete))
- FluentValidation on all inputs
- Global exception handling middleware
- Query execution timeout protection (supports unlimited/infinity)
- Full execution audit logging

## Quick Start with Docker

### Prerequisites

- Docker and Docker Compose installed
- Minimum 4GB RAM available for containers

### Steps

```bash
# 1. Clone the repository
git clone <repository-url>
cd DotNetDBTasks

# 2. Create environment file
cp .env.example .env
# Edit .env and set a strong JWT_SECRET, DB_PASSWORD, and ENCRYPTION_KEY

# 3. Build and start all services
docker compose build
docker compose up -d

# 4. Access the application
# Frontend: http://localhost
# API Swagger: http://localhost:5000/swagger
```

The Compose stack starts four services: **oracle** (Oracle XE 21c), **ldap** (OpenLDAP),
**api** (.NET 10), and **client** (Angular served by nginx).

## Local Development (without Docker)

### Prerequisites

- .NET 10 SDK
- Node.js 24.x and npm 10.x
- A reachable database (Oracle, SQL Server, MySQL, or PostgreSQL)
- A reachable LDAP/AD server for authentication

### Run the API

```bash
dotnet restore DotNetDBTasks.sln
dotnet run --project src/DotNetDBTasks.API
# Listens on http://localhost:5000 (Swagger at /swagger)
```

### Run the frontend

```bash
cd client
npm install
npm start          # ng serve on http://localhost:4200, proxies /api to the .NET API
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DB_PASSWORD` | Oracle database password | `YourStrong@Passw0rd` |
| `JWT_SECRET` | JWT signing key (min 32 chars) | Must be changed |
| `JWT_ISSUER` | JWT token issuer | `DotNetDBTasks` |
| `JWT_AUDIENCE` | JWT token audience | `DotNetDBTasks` |
| `ENCRYPTION_KEY` | Base64-encoded 32-byte AES-256 key for credential encryption | Must be changed |
| `LDAP_ADMIN_PASSWORD` | LDAP admin bind password | `LdapAdmin@123` |
| `APP_DOMAIN` | Public domain / allowed CORS origin | `https://tasks.example.com` |
| `CLIENT_PORT` | Angular app host port | `80` |

Generate a key with `openssl rand -base64 32`.

## Deployment

- **Docker / internet-connected:** see the Quick Start above.
- **Air-gapped (no Docker, no internet):** see [DEPLOY-AIRGAPPED.md](DEPLOY-AIRGAPPED.md) —
  covers both deploying a pre-built release **and** setting up a machine to edit and rebuild offline.

### Production checklist

- [ ] Set a strong, unique `JWT_SECRET` (minimum 32 random characters)
- [ ] Set a strong `DB_PASSWORD`
- [ ] Set a unique `ENCRYPTION_KEY` (base64-encoded 32 bytes) — losing it makes stored credentials unrecoverable
- [ ] Point `Ldap.Host` at the correct AD/LDAP server and verify a test login
- [ ] Configure HTTPS (reverse proxy with TLS termination)
- [ ] Set up log rotation for the Serilog file sink
- [ ] Configure backup for the database volume
- [ ] Restrict exposed ports via firewall rules

## API Endpoints

### Authentication
- `POST /api/auth/login` — Authenticate (via LDAP/AD) and get tokens
- `POST /api/auth/refresh` — Refresh expired access token

### Admin (requires Admin role)
- `GET /api/admin/dynamicqueries` — List all queries
- `GET /api/admin/dynamicqueries/{id}` — Get query by ID
- `POST /api/admin/dynamicqueries` — Create query
- `PUT /api/admin/dynamicqueries/{id}` — Update query
- `DELETE /api/admin/dynamicqueries/{id}` — Delete query
- `POST /api/admin/dynamicqueries/{id}/roles` — Assign roles
- `GET /api/admin/dynamicqueries/logs` — Get execution logs. Optional filters: `queryId`, `userId`, `isSuccess`, `queryType` (0=Select, 1=Insert, 2=Update, 3=Delete, 4=Other), `search`; plus `sortBy`/`sortDescending`/`pageNumber`/`pageSize`. All applied in the database.
- `GET /api/admin/roles` — List all roles

- `GET /api/admin/dynamicqueries/{id}/export` — Export one query as JSON
- `GET /api/admin/dynamicqueries/export` — Export every query as one JSON backup
- `POST /api/admin/dynamicqueries/import` — Restore from an export file, multipart `file` (Admin only)

### Branding
- `GET /api/branding/logo` — The site logo image, or 404 when none is set. **Anonymous** — see [Site logo](#site-logo)
- `GET /api/branding/logo/info` — `{ hasLogo, fileName, updatedAt }` (authenticated)
- `POST /api/branding/logo` — Upload/replace the logo, multipart `file` (Admin only)
- `DELETE /api/branding/logo` — Remove the logo (Admin only)

### User (requires authentication)
- `GET /api/user/queries` — Get queries assigned to user
- `POST /api/user/queries/{id}/execute` — Execute a normal query synchronously. A read result is cached server-side and the response returns execution metadata plus a `jobId`; a write result is returned inline (preview/confirm).
- `POST /api/user/queries/{id}/execute-async` — Submit a long-running query as a background job (returns `{ jobId }`)
- `GET /api/user/queries/jobs/{jobId}` — Poll an async job's status and result metadata (owner/Admin only)
- `GET /api/user/queries/jobs/{jobId}/rows` — Get one page of a cached read result with server-side `pageIndex`/`pageSize`/`sortColumn`/`sortDir`/`filters` (owner/Admin only)
- `GET /api/user/queries/jobs/{jobId}/export-file` — Download the cached read result as an Excel (.xlsx) file — no re-run (owner/Admin only)
- `POST /api/user/queries/jobs/{jobId}/cancel` — Cancel a running async job, stopping the query server-side (owner/Admin only)
- `DELETE /api/user/queries/jobs/{jobId}` — Release a cached result immediately (called when leaving the results page; owner/Admin only)
- `GET /api/user/queries/history` — Get execution history

## How Long-Running Queries Work (Async Execution)

A synchronous request holds the HTTP connection open for the entire query, so a slow query
is killed by whatever proxy/edge timeout sits in front of the app (nginx `proxy_read_timeout`
~120s, Cloudflare's edge cap ~100s) regardless of the query's own `TimeoutSeconds`. To avoid
this, an admin can flag a query as **long-running**, which switches it to an asynchronous
**submit-and-poll** flow where every HTTP request stays short.

### Choosing the mode

Each query has an `IsLongRunning` flag (a toggle on the admin query form; defaults to off):

- **Off (default) — synchronous.** `POST /api/user/queries/{id}/execute` runs the query and
  returns the result in the same response. Instant for normal, quick queries; no polling.
- **On — asynchronous.** The client calls `POST /api/user/queries/{id}/execute-async`, which
  enqueues a background job and returns `{ jobId }` immediately, then polls
  `GET /api/user/queries/jobs/{jobId}` every 2s until the job reaches a terminal state.

### Background execution

- Submitted jobs are held in an in-memory store (sliding retention — see
  [Result Paging & Export](#how-result-paging--export-work)) and drained by a `QueryJobWorker`
  `BackgroundService` that runs up to **4 jobs in parallel**.
- Each job executes on a fresh DI scope and **replays the same `ExecuteQueryCommand`** as the
  synchronous path, so access control, the preview-and-confirm flow, and audit logging are
  identical — only the thread it runs on differs. The submitting user's identity is carried
  to the worker via an `AsyncLocal` snapshot.
- Because the work runs off the request thread, no proxy/edge/browser timeout applies to the
  query itself; the only effective limit is the query's own `TimeoutSeconds`.

### Cancel & progress

The client shows a **live elapsed timer** while polling and a **Cancel** button that calls
`POST /api/user/queries/jobs/{jobId}/cancel`. Cancellation is real: a per-job cancellation
token flows into the database command, stopping the query and freeing the connection (not just
abandoning the poll). Only the job's owner (or an Admin) may poll or cancel it.

> **Note:** the in-memory job store fits a single API instance. Running multiple API
> containers would require a shared/persistent store (DB or Redis) so a poll can reach the
> node holding the job.

## How Result Paging & Export Work

A read query runs **once**. The full, uncapped result set is cached server-side (in the same
job store), and the execute/poll response returns only lightweight metadata plus the `jobId` —
never the rows. From that single execution:

- **The grid pages server-side.** On every page, sort, or column-filter change the client calls
  `GET jobs/{jobId}/rows`, which filters (per-column, case-insensitive "contains"), sorts, and
  slices the cached rows, returning just that page plus the filtered total. Only one page of data
  ever crosses the wire, so a large result never has to be shipped to the browser.
- **Export reuses the same cache.** `GET jobs/{jobId}/export-file` builds the workbook from the
  cached rows — it does **not** re-run the query.

This means viewing then exporting a slow query costs a single database execution, not two.

**Retention.** A cached result lives until the client leaves the results page — the page issues
`DELETE jobs/{jobId}` on navigation away, freeing the memory at once. As a fallback (missed signal,
tab close, crash) the store keeps each result under a **sliding** window that resets on every
poll/page/export and defaults to **1 day**, tunable via `ResultCache:RetentionMinutes`. The
trade-off is memory: full result sets are held for that window, which suits a single API instance —
a multi-instance deployment would move the cache to a shared/persistent store.

## How Write Queries Work (INSERT / UPDATE / DELETE)

The platform executes more than read-only `SELECT`s. A stored query can be an `INSERT`,
`UPDATE`, or `DELETE`, and these go through a deliberate **two-phase preview-and-confirm**
flow so a user can review the impact before any data actually changes.

### Statement classification

Each query's type is decided **once, at save time** — `QueryTypeClassifier.FromSql` reads the
leading keyword and the result is stored on `DynamicQueries.QueryType`
(`Select`/`Insert`/`Update`/`Delete`/`Other`). It is derived server-side on create and update,
never accepted from the client, so it cannot contradict the SQL it describes.

Storing it is not a speed optimisation — the keyword check is trivially cheap. It exists so the
type is available as an indexable column: the execution-log list filters and pages **in the
database**, so a type filter there could not otherwise participate in the query without a `LIKE`
scan over the `SqlQuery` CLOB. It also gives the execution path, the scheduled-task runner and
the Angular client one shared definition instead of a copy each.

`QueryType.Other` (a `MERGE`, DDL, a PL/SQL block) is deliberately **not** a write: such
statements have always fallen through as reads, and reclassifying them would newly subject them
to the preview/confirm handshake.

Two lower-level checks intentionally remain keyword-based, because they answer different
questions about raw SQL and never see a `DynamicQuery`: `QueryExecutor.IsSelectQuery` (does this
return rows? — it also accepts `WITH` for CTEs) and the `UPDATE`/`DELETE` branch inside
`FetchAffectedRowsPreviewAsync` (how do I parse a table and `WHERE` out of this statement?).

The runtime behaviour per type:

- `SELECT` / `WITH` → returns a result set (`Columns` + `Rows`). User-facing execution runs the
  read **once with no cap** and caches the full set server-side (see
  [How Result Paging & Export Work](#how-result-paging--export-work)); `MaxQueryRows` (default
  10,000) remains a safety cap for internal reads such as the write-preview `SELECT`.
- `INSERT` / `UPDATE` / `DELETE` → treated as a **write query**, run via
  `ExecuteNonQuery`, and the number of affected rows is returned in `AffectedRows`.

### Two-phase execution

The execute request accepts a `Confirmed` flag (default `false`) — on either the synchronous
`POST /api/user/queries/{id}/execute` or, for long-running queries, the async
`POST /api/user/queries/{id}/execute-async` (see [How Long-Running Queries Work](#how-long-running-queries-work-async-execution)).
For write queries the flow is:

1. **Preview (`Confirmed = false`).** The statement is opened inside a database
   transaction, executed to obtain the real affected-row count, and then **rolled back** —
   nothing is committed (`QueryExecutor.ExecutePreviewAsync`). The response comes back with
   `RequiresConfirmation = true` and `AffectedRows` set to the count that *would* change.
   - For `UPDATE`/`DELETE`, the handler additionally parses the table name and `WHERE`
     clause out of the statement and runs a derived `SELECT * FROM <table> WHERE <where>`
     so the actual rows about to be modified/deleted are returned in
     `PreviewColumns` / `PreviewRows`. (Parsing handles bare, `"quoted"`, and `[bracketed]`
     identifiers plus optional table aliases; if it can't parse the SQL, it simply skips the
     row preview rather than failing.)

2. **Confirm (`Confirmed = true`).** The client re-submits the same query and parameters
   with the confirm flag set. The statement now runs for real and **commits**.

The Angular client uses phase 1 to show the user the affected-row count (and, for
`UPDATE`/`DELETE`, the affected rows themselves) and asks for confirmation before
re-submitting with `Confirmed = true`.

### Skipping the preview

The preview costs two extra round trips and can be slow on large tables, so the execute page
offers write queries a **"Run directly without preview"** checkbox that sends `Confirmed = true`
on the first request and commits in one pass.

Whether that checkbox is offered is per query, controlled by the `AllowRunWithoutConfirmation`
toggle on the admin query form (**"Allow running without confirmation"**; defaults to on, so
existing queries are unaffected). With it off, the checkbox is not rendered and every run of
that query goes through the two-phase preview/confirm flow above.

This gates the UI affordance. It is not a server-side authorization check — `Confirmed = true`
is the same flag a legitimate phase-2 confirmation sends, so the API cannot tell the two apart
without tracking preview state. Enforcing it server-side would require issuing a preview token
in phase 1 and requiring it in phase 2.

Scheduled tasks are unaffected: they always run writes with `Confirmed = true`, since there is
no interactive user to confirm.

### Strict parameterization

No value is ever concatenated into SQL. Queries are authored with `@param` placeholders
and `[bracket]`-quoted identifiers, then adapted to the target engine at execution time:

| Server | Identifier quoting | Bind syntax |
|--------|-------------------|-------------|
| SQL Server | `[bracket]` (native) | `@param` (native) |
| Oracle | `"double-quote"` | `:param` (bind-by-name) |
| PostgreSQL | `"double-quote"` | `@param` |
| MySQL | `` `backtick` `` | `@param` |

All values are bound through the provider's `DbParameter` type. Multi-value parameters are
expanded so `WHERE col IN (@names)` becomes `IN (@names_0, @names_1, …)`, with each item
bound individually (an empty list expands to `IN (NULL)`, matching no rows).

### Access control & auditing

Before anything runs, the handler verifies the caller has access to the query (via role,
department, direct user assignment, or parent query-group assignment; Admins bypass) and to
the configured database user. Then:

- **Old-value capture.** For `UPDATE`/`DELETE`, the rows currently matching the `WHERE`
  clause are read and serialized into `OldValuesJson` on the execution log *before* the
  change commits, preserving the pre-change state for the audit trail.
  This is per query, controlled by the `SaveOldValues` toggle on the admin query form
  (**"Save before-change values"**; defaults to on). Turn it off for statements that affect
  large row counts — the capture costs an extra `SELECT` per run and stores a copy of every
  affected row. With it off, `OldValuesJson` stays null, so `HasOldValues` is false and the
  execution-log UI simply offers no before-values view for those runs. The execution log
  itself is still written either way.
- **Execution log.** Every execution — preview failures, successes, and errors — writes a
  `QueryExecutionLog` row recording the user, parameters (JSON), duration, affected/returned
  row count, success flag, and any error message.

## Query Backup (Export / Import)

Each row in Manage Queries has an **Export** button that downloads that query as JSON; the
toolbar's **Backup** menu offers **Export all queries** (one JSON file) and **Import from
backup…**. Import is Admin-only; export is available to anyone who can already read the query
list, since it exposes nothing they cannot already see.

### Everything travels by name, not by id

A backup restored onto a different system would carry GUIDs that resolve to nothing there, so
the file references related records by name and import re-resolves each one:

| Reference | On import |
|---|---|
| Query group | Matched by name; **created** if missing (a group is just a folder) |
| Database connection | Matched by name; if missing, the query is left on the default connection and a warning is reported |
| Roles / users | Matched by name; unmatched assignments are dropped and reported |
| Departments | Free text, carried across as-is |
| Dropdown source query | Resolved in a **second pass**, after every query in the file exists — so a dropdown can point at another query from the same backup, even one that got renamed |

`QueryType` is not exported: it is re-derived from the SQL on import rather than trusted from an
external file. Word templates ride along base64-encoded, so a restored query produces identical
Word exports.

### Credentials are never exported

A query records *which* database connection it uses, and that connection's password is encrypted
at rest. Writing it into a file an admin then emails or commits to source control would undo that
protection, so the export carries only the connection **name**. After a restore on a new system,
configure the connection there (with its own credentials) and the name match reconnects it.

### Import never overwrites

A query whose name is already taken is imported as `Name (imported)` — nothing existing is
modified or deleted, so a mistaken import is undone by deleting what it added. The result dialog
lists every query imported, which ones were renamed, and every reference that could not be
resolved.

## Site logo

The top banner shows an uploaded logo in place of the app name. An Admin sets it from the
account menu (the same menu as Logout) → **Website logo**, which opens a dialog with a preview,
the recommended dimensions, and Upload / Replace / Remove. With no logo stored the banner falls
back to the app name, so it is never blank.

The image lives in `SystemTemplates` under the `branding-logo` key — the same keyed store as the
default Word template, so there is no new table. Its content type is derived from the uploaded
file's extension.

**Sizing.** The banner renders it at 40 px tall and caps it at 200 px wide (140 px below 1400 px,
where the nav labels collapse and the row is tight), with `object-fit: contain` preserving the
aspect ratio. The dialog recommends 80 px tall — 2x, so it stays sharp on HiDPI screens — and
warns before upload when the chosen image is shorter than the banner, below 2x, or so wide it
will be capped.

**Two deliberate choices worth knowing:**

- `GET /api/branding/logo` is **anonymous**. The banner loads it with a plain `<img src>`, and
  browsers do not run image requests through the app's JWT interceptor, so an authorized
  endpoint would just 401. A logo is public branding rather than protected data. Uploading and
  removing it still require the Admin role.
- **SVG is rejected**, despite being ideal for logos. An SVG can carry script that executes if
  its URL is opened directly, and this endpoint is same-origin and anonymous. Accepting it
  safely needs a restrictive CSP response header on that action, not just an extension check.
  Uploads are limited to PNG/JPG/WebP at 1 MB, and the bytes are checked against the format's
  magic number so the extension alone cannot decide what gets served back.

Uploads are cache-busted with `?v=<updatedAt>`; the image itself is sent with a long
`immutable` cache lifetime, since the bytes at any given URL never change.

## Database Schema

```
Users ──┐
        ├── UserRoles ──┐
Roles ──┘               │
  │                     │
  ├── DynamicQueryRoles ─── DynamicQueries ── QueryParameters
  │                              │
  └──────────────────── QueryExecutionLogs
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10 Web API |
| Frontend | Angular 21 + Angular Material |
| Database | Oracle XE 21c (SQL Server, MySQL, PostgreSQL also supported) |
| ORM | Entity Framework Core 10 |
| DB drivers | Oracle.EntityFrameworkCore, EFCore.SqlServer, MySqlConnector, Npgsql |
| CQRS | MediatR |
| Validation | FluentValidation |
| Mapping | AutoMapper |
| Logging | Serilog |
| Auth | LDAP/AD + JWT Bearer; AES-256 credential encryption |
| Containers | Docker + Docker Compose |
| Web Server | nginx (Angular) + Kestrel (.NET) |
