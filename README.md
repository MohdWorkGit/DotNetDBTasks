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
- `GET /api/admin/dynamicqueries/logs` — Get execution logs
- `GET /api/admin/roles` — List all roles

### User (requires authentication)
- `GET /api/user/queries` — Get queries assigned to user
- `POST /api/user/queries/{id}/execute` — Execute a normal query synchronously (returns the result)
- `POST /api/user/queries/{id}/execute-async` — Submit a long-running query as a background job (returns `{ jobId }`)
- `GET /api/user/queries/jobs/{jobId}` — Poll an async job's status and result (owner/Admin only)
- `POST /api/user/queries/jobs/{jobId}/cancel` — Cancel a running async job, stopping the query server-side (owner/Admin only)
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

- Submitted jobs are held in an in-memory store (results evicted after ~30 min) and drained
  by a `QueryJobWorker` `BackgroundService` that runs up to **4 jobs in parallel**.
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

## How Write Queries Work (INSERT / UPDATE / DELETE)

The platform executes more than read-only `SELECT`s. A stored query can be an `INSERT`,
`UPDATE`, or `DELETE`, and these go through a deliberate **two-phase preview-and-confirm**
flow so a user can review the impact before any data actually changes.

### Statement classification

Every statement is classified by its leading keyword (see `QueryExecutor` and
`ExecuteQueryCommandHandler`):

- `SELECT` / `WITH` → returns a result set (`Columns` + `Rows`), capped at `MaxQueryRows`
  (default 10,000; the result sets `IsLimitReached` when truncated).
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
- **Execution log.** Every execution — preview failures, successes, and errors — writes a
  `QueryExecutionLog` row recording the user, parameters (JSON), duration, affected/returned
  row count, success flag, and any error message.

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
