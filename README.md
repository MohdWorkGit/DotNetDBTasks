# Bayan

A production-ready dynamic database query execution platform built with .NET 10, Angular 21, Oracle, and Docker.

## Architecture

**Clean Architecture + CQRS** with strict layer separation:

```
src/
├── Bayan.Domain          # Entities, interfaces, enums, exceptions (zero dependencies)
├── Bayan.Application     # CQRS commands/queries, DTOs, validators, mappings
├── Bayan.Infrastructure  # EF Core, repositories, JWT, encryption, query executor
└── Bayan.API             # Controllers, middleware, DI configuration
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
| Assign queries to roles, user groups or individual users | View personal execution history |
| Edit what every role may do, and add custom roles | |
| Maintain user groups in the app — no dependency on AD groups | |
| Enable/disable queries, configurable timeouts | |
| Flag slow queries as long-running (async execution) | Long-running queries run as background jobs — live elapsed timer + cancel |
| Allow or block running writes without confirmation | Skip the write preview when the query allows it |
| Turn off before-change row capture on bulk writes | |
| Filter queries and execution logs by statement type | |
| Set a site logo, a site name per language, and the browser tab icon | |
| Export queries to JSON and import them back / on a new system | |
| View execution audit logs | |

## Security

- LDAP / Active Directory authentication
- Windows SSO (Kerberos/NTLM) when hosted on IIS — domain users are signed in automatically
  without a form; off by default via `Auth:EnableSso`
- Per-query export control — each query lists which formats it may be downloaded as, and each
  role holds which formats it may use; see [Query export permissions](#query-export-permissions)
- JWT access + refresh token authentication
- AES-256 encryption of stored database credentials at rest
- Role-based authorization — see [Roles](#roles)
- Parameterized SQL only — no string concatenation
- SQL query validation (forbidden pattern detection: `XP_`, `SP_`, `--`, `;`, `DBMS_`, `UTL_`)
- Write queries (INSERT/UPDATE/DELETE) run preview-and-confirm: previewed in a rolled-back
  transaction before any commit (see [How Write Queries Work](#how-write-queries-work-insert--update--delete))
- FluentValidation on all inputs
- Global exception handling middleware
- Query execution timeout protection (supports unlimited/infinity)
- Full execution audit logging

## Roles and permissions

Authorization is a **role → permission matrix**, edited at **Settings → Permissions**. Endpoints
name a capability (`[RequirePermission(Permissions.QueriesRun)]`), not a role, so who reaches
what is a runtime decision rather than a compiled one — and a role an installation invents is a
first-class citizen rather than something the code has never heard of.

Four roles are seeded, and any number can be added. A user may hold several; permissions add up.

| Role | Holds by default |
|---|---|
| **Admin** | Everything, always — see the pinning rule below |
| **User** | `queries.run` |
| **Auditor** | `logs.view`, `audit.view`, `scheduledTasks.viewAll` |
| **Access Manager** | `queries.view`, `access.manageGroup`, `userGroups.view`, `userGroups.manage`, `users.view`, `users.manage`, `directory.view` |

### The 27 capabilities

| Area | Permissions |
|---|---|
| Queries | `queries.view`, `queries.readSql`, `queries.manage`, `queries.transfer`, `queries.run` |
| Reports | `reports.view`, `reports.manage`, `reports.run` |
| Granting access | `access.manageQuery`, `access.manageGroup`, `access.manageReport`, `queryGroups.manage` |
| People and directory | `users.view`, `users.manage`, `userGroups.view`, `userGroups.manage`, `directory.view`, `directory.manage` |
| Connections and tasks | `databaseUsers.manage`, `scheduledTasks.viewAll`, `scheduledTasks.manage`, `scheduledTasks.download` |
| Oversight | `logs.view`, `audit.view` |
| System | `branding.manage`, `settings.manage`, `roles.manage` |

The names are persisted in `RolePermissions`, so renaming one silently revokes it — they are a
contract, like the setting keys. Adding one is safe: no role holds it until someone ticks the
box, so a new capability starts closed everywhere.

### Rules that hold whatever the matrix says

- **Admin is pinned.** It holds every permission, its column is read-only, and it cannot be
  deleted. Without that, the last account able to open the Permissions tab could be edited out
  of it. `PermissionService` answers for Admin without consulting the table at all, so even an
  empty or half-written matrix leaves someone able to put it right.
- **Seeded roles cannot be renamed or deleted.** Their permissions are fully editable; the names
  are referenced by code, translations and the seeder.
- **A role still in use cannot be deleted.** Move its holders first — reassigning people is a
  decision, not a side effect.
- **Nobody grants themselves.** `AdminAccountGuard` stops a non-Admin editing their own roles,
  granting Admin, touching an administrator account, or adding themselves to a user group.
- **Permissions resolve per request**, never from the token. Revoking one takes effect on the
  caller's next click; nobody has to sign out. The cost is one indexed lookup per check, which is
  deliberate — see the note on caching under Runtime Settings.
- **Assigning a query to a role that cannot run queries does nothing.** `QueryAccessRoles` drops
  any role without `queries.run` when resolving access, so such a grant is inert rather than
  quietly broken.

Two consequences worth stating plainly, because earlier versions promised otherwise:

- `queries.run` **can** be granted to Auditor or Access Manager. It is not held by default, and
  granting it is a deliberate act, but the old "these two roles can never run a query, ever"
  guarantee is now a default rather than a law.
- The `accessManager.canManageQueryAccess` and `accessManager.canManageUserGroups` settings are
  gone. They were two hard-coded questions about one role; they are now the `access.manageQuery`
  and `userGroups.manage` cells, askable of any role. The migration carries an existing
  installation's answers across.

Seeded accounts (development only — change or remove before deploying):
`admin` / `Admin@123`, `user` / `User@123`, `auditor` / `Auditor@123`,
`accessmanager` / `Access@123`.

## Runtime Settings

Toggles an administrator can change without a restart, stored in `SystemSettings` and edited at
**Settings** (in the profile menu, needs `settings.manage`). Who may do what is *not* here — that
is the Permissions tab beside it, described under [Roles and permissions](#roles-and-permissions). They live in the database rather than
`appsettings.json` precisely so they can be flipped from the UI — restarts are a scheduled event
on the air-gapped installs. A missing row means "use the compiled default", so an upgraded
database behaves exactly like a fresh one until someone changes something.

| Key | Default | Effect |
|---|---|---|
| `session.accessTokenMinutes` | `60` | Access-token lifetime, 5–1440. Shorter means a revoked account or a dropped group membership stops working sooner. |
| `session.refreshTokenDays` | `7` | Refresh-token lifetime, 1–90. How long someone may stay away and still return without signing in again. |
| `query.maxRows` | `10000` | Rows read in one go on the paths that hold a whole result in memory — write previews, before-change snapshots, dropdown lookups. **Not** a display limit: the grid caches the full result and pages it, and exports are never capped. Overrides `MaxQueryRows` in appsettings.json, which remains the fallback. |
| `directory.enabled` | `true` | When off, the AD Users page is hidden and `/api/admin/ldap` returns 503. Signing in is deliberately unaffected, so flipping it cannot lock out imported accounts. |

The numbers are range-checked server-side; a value outside its bounds is refused with a message
naming them. Every change is audited (`settings.updated`) with the full set of values.

Two further rows live in this table but are **not** edited here: `branding.siteNameEn` and
`branding.siteNameAr` belong to the branding dialog and its own permission — see
[Site branding](#site-branding--logo-name-and-tab-icon).

Settings are read on authorization paths and deliberately **not cached** — a stale value would
mean granting access an administrator believes they just revoked. Changes are audited
(`settings.updated`) with the new value.

## System Audit Trail

Every administrative change — a user created, a permission granted, a query edited — is
recorded to `SystemAuditLogs` and shown on **System Audit** (`/admin/system-audit`), visible to
**Admin and Auditor**. Distinct from the execution logs: those answer *what did people run*,
this answers *what did people change*.

Entries are written by `AuditLoggingBehavior`, a MediatR pipeline behavior, so **every command
is covered without each handler remembering to log**. That completeness is the point — a trail
with silent gaps is not one. Consequences worth knowing:

- **A new command is audited automatically.** Name it in `AuditActions.Map` to give it a
  readable label; skip that and it still records, under the `other` category with its raw type
  name. Exclusions are a short explicit list (`AuditActions.IsExcluded`): auth commands, and
  query execution because it already has its own richer log.
- **Refusals are recorded too.** "Access Manager tried to reset the admin's password and was
  refused" is often the entry that matters, so the behavior logs the exception path as well as
  the happy one.
- **Secrets never reach the table.** Any property whose name contains `password`, `secret`,
  `token`, `connectionstring`, `encrypted`, `apikey` or `credential` is replaced with `***`
  before serialization; file bytes and other bulk fields become `[omitted]`.
- **Read-only.** The API exposes no write verb — `POST`/`PUT`/`DELETE`/`PATCH` all return 405.
  An audit trail an administrator can rewrite is not one.
- **Actions are stored as codes**, not sentences (`users.create`), and translated in the UI, so
  the trail reads in Arabic too. Add a code to `admin.audit.actions` in **both** catalogs.

Two current limitations: there is **no retention or pruning** — the table only grows, so plan a
housekeeping job before it matters. And a recorded `errorMessage` is frozen in whatever language
the actor was using at the time, because it stores the produced message rather than a code.

## Localization (English / Arabic)

The UI ships in English and Arabic, switchable at runtime from the toolbar (and from the login
page, so someone who cannot read the English form can switch before signing in). One build serves
both — there is no per-locale bundle and no nginx locale routing.

| Layer | Mechanism |
|---|---|
| Client strings | [Transloco](https://jsverse.github.io/transloco/) — `client/src/assets/i18n/{en,ar}.json` |
| Active locale + direction | `core/services/language.service.ts`, modelled on `ThemeService`; sets `lang`/`dir` on `<html>` |
| Angular Material mirroring | Driven by `dir` on `<html>` via `@angular/cdk/bidi` — no per-component RTL config |
| Server messages | `Resources/Messages.resx` / `Messages.ar.resx` + `IAppLocalizer`, selected by `Accept-Language` |
| Arabic typography | Cairo via `@fontsource/cairo`, `line-height: 1.75` under `:root[lang='ar']` |

Adding a string: put the key in **both** `en.json` and `ar.json` and reference it with the
`transloco` pipe. `ToastService` and `ConfirmService` resolve keys themselves, so a message is
`toast.success('admin.users.created')` — no component injects `TranslocoService` just to show one.

Four conventions that are easy to get wrong:

- **Never concatenate a translated fragment with a variable.** Use interpolation
  (`'admin.users.greeting' | transloco: { name }`), because word order differs between the two
  languages and concatenation bakes in English order.
- **Wrap embedded LTR values in bidi isolates.** Arabic catalog entries carrying a username, file
  name or host use `⁨{{param}}⁩` (U+2068 / U+2069). In templates, an element attribute works too —
  `<span dir="ltr">`— but attributes are invisible inside an `aria-label`, where only the isolate
  characters survive.
- **Content the app did not author gets `dir="auto"`.** Query names, group names, descriptions and
  every result-grid cell: this database already holds a query named `كل المستخدمين`, and it has to
  render correctly in the English UI too. Conversely SQL, connection strings and paths take
  `.force-ltr`, never `auto`.
- **Use CSS logical properties.** `margin-inline-start`, not `margin-left`; `text-align: start`,
  not `left`. CI-style gate: `grep -rE 'margin-left|margin-right|padding-left|padding-right' client/src`
  must return nothing.

Technical vocabulary stays in Latin script inside Arabic text — SQL, API, LDAP, Active Directory,
CSV/XLSX, and the role names `Admin` / `Auditor` / `AccessManager` — because transliterating them
makes the UI harder for the technical audience that uses it.

**Not covered:** Arabic-Indic numerals, Hijri dates, and RTL layout inside exported
PDF/Word/Excel files (export rendering goes through LibreOffice/Word templates).

## Quick Start with Docker

### Prerequisites

- Docker and Docker Compose installed
- Minimum 4GB RAM available for containers

### Steps

```bash
# 1. Clone the repository
git clone <repository-url>
cd Bayan

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
dotnet restore Bayan.sln
dotnet run --project src/Bayan.API
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
| `JWT_ISSUER` | JWT token issuer | `Bayan` |
| `JWT_AUDIENCE` | JWT token audience | `Bayan` |
| `ENCRYPTION_KEY` | Base64-encoded 32-byte AES-256 key for credential encryption | Must be changed |
| `LDAP_ADMIN_PASSWORD` | LDAP admin bind password | `LdapAdmin@123` |
| `APP_DOMAIN` | Public domain / allowed CORS origin | `https://tasks.example.com` |
| `CLIENT_PORT` | Angular app host port | `80` |

Generate a key with `openssl rand -base64 32`.

## Deployment

- **IIS on Windows Server (single site, with Windows SSO):** `./scripts/deploy-iis.ps1 -Hostname bayan.corp.local -EnableSso`,
  run elevated on the server. One site serves both the SPA and the API, so there is no reverse
  proxy and no CORS, and domain users are signed in automatically. Full walkthrough in
  [DEPLOY-AIRGAPPED.md](DEPLOY-AIRGAPPED.md) Part C.
- **Docker / internet-connected:** see the Quick Start above.
- **Air-gapped (no Docker, no internet):** see [DEPLOY-AIRGAPPED.md](DEPLOY-AIRGAPPED.md) —
  covers both deploying a pre-built release **and** setting up a machine to edit and rebuild offline.

> **The background workers live in the web process.** Scheduled exports, async query jobs and
> cached results all run inside the API's own process, so whatever hosts it must keep it running
> continuously and must run exactly one copy of it. Under IIS that means specific app pool
> settings — see [DEPLOY-AIRGAPPED.md](DEPLOY-AIRGAPPED.md) Part C0 and C3.5.

### Production checklist

- [ ] Set a strong, unique `JWT_SECRET` (minimum 32 random characters)
- [ ] Set a strong `DB_PASSWORD`
- [ ] Set a unique `ENCRYPTION_KEY` (base64-encoded 32 bytes) — losing it makes stored credentials unrecoverable
- [ ] Point `Ldap.Host` at the correct AD/LDAP server and verify a test login
- [ ] Configure HTTPS — a binding and certificate on the IIS site, or TLS termination at the
      reverse proxy for the nginx/Docker models
- [ ] Confirm the background workers survive an idle period (see Part C5 — leave the site alone
      for 30 minutes, then check that a scheduled task still fired)
- [ ] Set up log rotation for the Serilog file sink
- [ ] Configure backup for the database volume
- [ ] Restrict exposed ports via firewall rules

## API Endpoints

**Full reference: [docs/API-REFERENCE.md](docs/API-REFERENCE.md)** — all 97 endpoints with their
verbs, roles and query parameters. The map below is for orientation only.

| Area | Base path | Who reaches it |
|---|---|---|
| Authentication | `/api/auth` | Anonymous — login, refresh, Windows SSO |
| Branding | `/api/branding` | Any authenticated user; the logo image itself is anonymous |
| Queries | `/api/admin/dynamicqueries` | Admin; AccessManager for accessibility, Auditor for logs |
| Query groups | `/api/admin/querygroups` | Admin creates and edits; AccessManager grants |
| User groups | `/api/admin/usergroups` | Admin and AccessManager |
| Users and roles | `/api/admin/users`, `/api/admin/roles` | Admin and AccessManager |
| Active Directory | `/api/admin/ldap` | Admin (two read endpoints also AccessManager) |
| Database connections | `/api/admin/databaseusers` | Admin — except `/accessible`, open to any authenticated user |
| Scheduled tasks | `/api/scheduledtasks` | Admin manages; viewers read the tasks granted to their role, user group, or name |
| Audit trail | `/api/admin/systemauditlogs` | Admin and Auditor, read-only |
| System settings | `/api/admin/systemsettings` | Admin writes; AccessManager reads |
| Running queries | `/api/user/queries` | Any authenticated user, limited to what they may run |

Endpoints name a **permission**, not a role — see [Roles and permissions](#roles-and-permissions).
Two rules are enforced in the handlers rather than by an attribute, so they are invisible in a
route list: `AdminAccountGuard` fences off administrator accounts and self-grants, and a role
without `queries.run` never resolves query access however a query is assigned to it. The
reference marks each one.

## How Long-Running Queries Work (Async Execution)

A synchronous request holds the HTTP connection open for the entire query, so a slow query
is killed by whatever timeout sits in front of the app (nginx `proxy_read_timeout` ~120s,
Cloudflare's edge cap ~100s, IIS `connectionTimeout` ~120s) regardless of the query's own
`TimeoutSeconds`. To avoid this, an admin can flag a query as **long-running**, which switches
it to an asynchronous **submit-and-poll** flow where every HTTP request stays short.

This is also why a long query survives IIS hosting: the browser only ever makes 2-second poll
requests, so no front-end timeout applies to the query itself. What the query does *not* survive
is the worker process restarting — the job store is in-memory, so a recycle mid-query loses the
result and the client is told the result is no longer available and to run it again. Part C3.5
of [DEPLOY-AIRGAPPED.md](DEPLOY-AIRGAPPED.md) configures IIS so those restarts do not happen on
their own.

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

## Query and report export permissions

Downloading a result is gated **twice**, and a download needs both gates to agree:

1. **The query** lists the formats its results may be exported as at all (Excel, CSV, PDF, Word,
   JSON) — set on the query's edit form under *Result export*. Tick nothing and the query cannot
   be downloaded by anyone; the Export button does not appear.
2. **The role** holds which formats that person may use, as five capabilities on the Roles &
   Permissions page (`queries.exportExcel`, `…Csv`, `…Json`, `…Pdf`, `…Word`).

Reports use the same two-gate rule against their own pair: `Report.AllowedExportFormats` and
`reports.exportExcel` / `…Csv` / `…Json` / `…Pdf` / `…Word`. Holding the query capability does
not confer the report one.

Neither alone grants anything. A query that permits PDF gives nothing to a role without
`queries.exportPdf`, and a role holding every export capability still cannot download a query
that permits no formats. The export menu shows the intersection, and
`GET /api/user/queries/jobs/{jobId}/export-file` re-checks both — hiding a menu item is not a
control, and that endpoint is reachable directly.

> **Upgrading closes export everywhere.** The `AddQueryExportPermissions` migration gives every
> existing query an empty format list and grants no role any export capability, so downloads stop
> working until an administrator opens them. This is deliberate — the alternative silently keeps
> exporting data that may be exactly what you wanted to restrict — but it does mean the first
> job after upgrading is to walk the query list and the permission matrix.

**Scheduled tasks are not affected.** They write files server-side from a definition that already
requires `scheduledTasks.manage`, and gating them on the same lists would stop every existing
export job the moment the migration ran. A consequence worth knowing: someone who can create a
scheduled task can still produce a file from a query whose interactive export is switched off.
If that matters in your installation, restrict `scheduledTasks.manage` accordingly.

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
user group, direct user assignment, or parent query-group assignment; Admins bypass) and to
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

## User groups

Access is granted to **roles**, to **user groups**, or to individual **users**. User groups are
maintained inside the application, on **Users & Access → User Groups**, and are deliberately not
a mirror of Active Directory.

Access used to hang off the AD `department` attribute. That coupled every permission decision to
a directory this application does not own: a reorganisation upstream silently moved people in and
out of access, locally created accounts (which have no directory entry) could never be grouped at
all, and reading who could see a query meant inferring it from an attribute nobody here maintains.
A group is now an explicit list of members, and the same group can hold AD-imported and local
accounts side by side.

- **Membership resolves per request**, not from the JWT. Removing someone from a group takes their
  access away on their next click, without waiting for the hour-long token to expire.
- **Access Managers own them.** Deciding who is in a group is the same job as deciding what a
  group may reach, so the role that does one does both. Query groups are a different thing and
  stay Admin-only to create, edit and delete.
- **Nobody can add themselves.** `AdminAccountGuard.EnsureNotJoiningGroup` refuses a non-Admin
  who puts their own account into a group — otherwise an Access Manager could grant a query to a
  group and then walk into it, which is exactly the self-grant the role-set rule already blocks.
  Joining is refused, not membership: an account an administrator already placed in the group
  stays there when the group is saved, and may still leave it.
- **Deleting a group** removes its grants too. The member accounts are untouched.
- Every change is audited under the `userGroups` category (`userGroups.create`, `.update`,
  `.delete`, `.setMembers`), and grants under `access.queryUserGroups` / `access.groupUserGroups`.

The AD department is still recorded on imported accounts and still drives the **AD Users** page,
where you browse the directory by department and import people from it. It just no longer decides
what anyone can reach.

Upgrading from a department-based install carries access across: the
`SwitchAccessFromAdDepartmentsToUserGroups` migration turns every department that currently grants
something into a user group of the same name, seeded with the users whose department matched at
that moment, and re-points the grants at it. Nobody's access changes on the day of the upgrade;
from then on, membership is edited in the application.

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
| User groups | Matched by name; unmatched grants are dropped and reported (membership is never exported) |
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

## Site branding — logo, name and tab icon

The top banner shows an uploaded logo, or the site's name written per language, in place of the
app name. An Admin sets both from the account menu (the same menu as Logout) → **Website
branding**, one dialog holding the logo (preview, recommended dimensions, Upload / Replace /
Remove) above the name in each language.

**The banner resolves three things in order** — logo, then the site name for the active
language, then the translated app name. So it is never blank, and removing a logo reveals the
name underneath rather than emptying the banner. Both halves live in one dialog for that
reason: split across two places, an admin would have to guess which one is currently winning.

**Two names, not one.** `branding.siteNameEn` and `branding.siteNameAr` are separate rows,
because an installation is rarely called the same thing in both languages — a single value
would force one audience to read the other's name. Either may be left blank on its own, so
naming an installation in Arabic only is a valid setup; the English UI then falls back to the
app name. Each is capped at 60 characters, refused rather than truncated, since the banner
shares its width with the navigation.

They are stored in `SystemSettings` but **edited under `branding.manage`, not
`settings.manage`** — naming the site is a branding decision, not a system toggle, so they do
not appear on the Settings page. Clearing one deletes its row rather than blanking it: Oracle
stores `''` as NULL and the `Value` column is NOT NULL, and "no row" is already what that
table means by unset.

**The browser tab icon** is set in the same dialog and is deliberately independent of all
three. A banner logo is wide and legible at 40 px tall; a favicon is a 16 px square, and
shrinking one into the other reliably produces a smudge — so it is uploaded separately. With
none stored the browser shows its own default, which is exactly what it did before the feature
existed: `index.html` ships no `<link rel="icon">`, and the client adds one pointing at the
endpoint.

`GET /api/branding/favicon` is anonymous for a stronger reason than the logo: the browser
fetches the tab icon for the **sign-in page**, before anyone has a token. It is served
`no-cache` rather than `immutable`, because that first paint has no `updatedAt` to cache-bust
with; once the shell knows the timestamp it re-stamps the link, which is what dislodges the
icon Chrome otherwise holds well past its headers. Uploads are capped at 256 KB — a quarter of
the logo's allowance, since every page load fetches it before anything is painted — and accept
PNG, ICO or WebP. **ICO replaces JPG** in the accepted set: a favicon needs the transparency
JPG cannot carry, and ICO is what most icon tooling still emits. An ICO must declare image
type 1; a cursor shares the container and would otherwise pass.

Both images live in `SystemTemplates`, under the `branding-logo` and `branding-favicon` keys —
the same keyed store as the default Word template, so there is no new table. Content types are
derived from the uploaded file's extension.

**Sizing.** The banner renders it at 40 px tall and caps it at 200 px wide (140 px below 1400 px,
where the nav labels collapse and the row is tight), with `object-fit: contain` preserving the
aspect ratio. The dialog recommends 80 px tall — 2x, so it stays sharp on HiDPI screens — and
warns before upload when the chosen image is shorter than the banner, below 2x, or so wide it
will be capped.

**Two deliberate choices worth knowing:**

- `GET /api/branding/logo` is **anonymous**. The banner loads it with a plain `<img src>`, and
  browsers do not run image requests through the app's JWT interceptor, so an authorized
  endpoint would just 401. A logo is public branding rather than protected data. Uploading and
  removing it still require `branding.manage`. The site name is *not* anonymous: it rides on
  `GET /api/branding`, which the shell already calls after sign-in.
- **SVG is rejected**, despite being ideal for logos. An SVG can carry script that executes if
  its URL is opened directly, and this endpoint is same-origin and anonymous. Accepting it
  safely needs a restrictive CSP response header on that action, not just an extension check.
  Uploads are limited to PNG/JPG/WebP at 1 MB, and the bytes are checked against the format's
  magic number so the extension alone cannot decide what gets served back.

Uploads are cache-busted with `?v=<updatedAt>`; the image itself is sent with a long
`immutable` cache lifetime, since the bytes at any given URL never change.

## Database Schema

```
Users ──┬── UserRoles ─────── Roles
        │                       │
        ├── UserGroupMembers ── UserGroups
        │                       │
        │        ┌──────────────┴──────────────┐
        │        │                             │
        │  DynamicQueryUserGroups      QueryGroupUserGroups
        │        │                             │
        │        └── DynamicQueries ── QueryParameters
        │                 │      └──── QueryGroups
        │                 │
        └──────────── QueryExecutionLogs

(DynamicQueryRoles / DynamicQueryUsers and their QueryGroup twins grant the same queries
 to a role or to one person; user groups are the third way in.)
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
| Web Server | IIS single site (in-process), or nginx (Angular) + Kestrel (.NET) |
