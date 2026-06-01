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
| View execution audit logs | |

## Security

- LDAP / Active Directory authentication
- JWT access + refresh token authentication
- AES-256 encryption of stored database credentials at rest
- Role-based authorization (Admin, User)
- Parameterized SQL only — no string concatenation
- SQL query validation (SELECT-only, forbidden pattern detection)
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
- `POST /api/user/queries/{id}/execute` — Execute a query
- `GET /api/user/queries/history` — Get execution history

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
