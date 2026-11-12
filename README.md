# Jadwal

Run saved database queries, reports and dashboards, with permissions, scheduling, an audit trail and an
optional local AI assistant. .NET 10, Angular 21, Oracle (SQL Server, PostgreSQL and MySQL also work),
in English and Arabic.

## Documentation

Open **[docs/index.html](docs/index.html)** in a browser. It works straight from disk, with no internet,
in English and Arabic, light and dark.

| Page | For |
|---|---|
| [Features](docs/features.html) | What Jadwal does |
| [Security and permissions](docs/security.html) | Roles, the permission matrix, settings, the audit trail |
| [AI assistant](docs/ai.html) | The local AI model: features, safety, models |
| [Deployment](docs/deploy.html) / [IIS](docs/iis.html) | Installing, offline bundles, upgrading |
| [Troubleshooting](docs/troubleshooting.html) | Known problems and what to check |
| [API](docs/api.html) / [Database](docs/database.html) / [Development](docs/development.html) | For developers |

Screen-by-screen user manuals: `docs/user-manual/Jadwal-User-Manual-EN.docx` and `-AR.docx`.

## Quick start

```bash
# Docker: copy .env.example to .env and set the secrets first
docker compose up -d --build                     # http://localhost

# Or locally
dotnet run --project src/Jadwal.API              # http://localhost:5000/swagger
cd client && npm install && npm start            # http://localhost:4200
```
