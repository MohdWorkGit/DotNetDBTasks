# DotNetDBTasks

A production-ready dynamic database query execution platform built with .NET 8, Angular 18, SQL Server, and Docker.

## Architecture

**Clean Architecture + CQRS** with strict layer separation:

```
src/
├── DotNetDBTasks.Domain          # Entities, interfaces, enums, exceptions (zero dependencies)
├── DotNetDBTasks.Application     # CQRS commands/queries, DTOs, validators, mappings
├── DotNetDBTasks.Infrastructure  # EF Core, repositories, JWT, password hashing, query executor
└── DotNetDBTasks.API             # Controllers, middleware, DI configuration
client/                           # Angular 18 SPA with Angular Material
docker/                           # Dockerfiles and nginx config
```

**Key patterns:** Repository, Unit of Work, MediatR (CQRS), AutoMapper, FluentValidation, Serilog.

## Features

| Admin | User |
|-------|------|
| Create/edit/delete parameterized SQL queries | View queries assigned to their roles |
| Define input parameters (string, number, date, boolean) | Fill dynamically generated forms |
| Assign queries to roles | Execute queries with pagination |
| Enable/disable queries | Export results to CSV |
| View execution audit logs | View personal execution history |

## Security

- JWT access + refresh token authentication
- BCrypt password hashing (work factor 12)
- Role-based authorization (Admin, User)
- Parameterized SQL only — no string concatenation
- SQL query validation (SELECT-only, forbidden pattern detection)
- FluentValidation on all inputs
- Global exception handling middleware
- Query execution timeout protection
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
# Edit .env and set a strong JWT_SECRET and DB_PASSWORD

# 3. Build and start all services
docker compose build
docker compose up -d

# 4. Access the application
# Frontend: http://localhost
# API Swagger: http://localhost:5000/swagger
```

### Default Credentials

| Username | Password | Role |
|----------|----------|------|
| admin | Admin@123 | Admin |
| user | User@123 | User |

**Change these immediately in production.**

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DB_PASSWORD` | SQL Server SA password | `YourStrong@Passw0rd` |
| `DB_PORT` | SQL Server host port | `1433` |
| `JWT_SECRET` | JWT signing key (min 32 chars) | Must be changed |
| `JWT_ISSUER` | JWT token issuer | `DotNetDBTasks` |
| `JWT_AUDIENCE` | JWT token audience | `DotNetDBTasks` |
| `API_PORT` | API host port | `5000` |
| `CLIENT_PORT` | Angular app host port | `80` |

## Production Deployment

### Build and tag images

```bash
# Build images
docker compose build

# Tag for private registry
docker tag dotnetdbtasks-api:latest your-registry.com/dotnetdbtasks-api:1.0.0
docker tag dotnetdbtasks-client:latest your-registry.com/dotnetdbtasks-client:1.0.0

# Push to registry
docker push your-registry.com/dotnetdbtasks-api:1.0.0
docker push your-registry.com/dotnetdbtasks-client:1.0.0
```

### Deploy on remote server

```bash
# On the VPS:
# 1. Pull the repository
git clone <repository-url>
cd DotNetDBTasks

# 2. Configure environment
cp .env.example .env
nano .env  # Set production values

# 3. Build and run
docker compose build
docker compose up -d

# 4. Verify health
docker compose ps
docker compose logs -f api
```

### Production checklist

- [ ] Set a strong, unique `JWT_SECRET` (minimum 32 random characters)
- [ ] Set a strong `DB_PASSWORD`
- [ ] Change default user passwords after first login
- [ ] Configure HTTPS (reverse proxy with TLS termination)
- [ ] Set up log rotation for container logs
- [ ] Configure backup for the `sqlserver_data` volume
- [ ] Restrict exposed ports via firewall rules

## API Endpoints

### Authentication
- `POST /api/auth/login` — Authenticate and get tokens
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
| Backend | .NET 8 Web API |
| Frontend | Angular 18 + Angular Material |
| Database | SQL Server 2022 |
| ORM | Entity Framework Core 8 |
| CQRS | MediatR |
| Validation | FluentValidation |
| Mapping | AutoMapper |
| Logging | Serilog |
| Auth | JWT Bearer + BCrypt |
| Containers | Docker + Docker Compose |
| Web Server | nginx (Angular) + Kestrel (.NET) |
