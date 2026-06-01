# Air-Gapped Guide (No Docker, No Internet)

This guide covers two scenarios for an isolated network with no internet access:

- **Part A — Deploy a pre-built release.** You build on an internet-connected machine and copy
  the output across. The target only needs to *run* the app.
- **Part B — Edit and rebuild offline.** The target machine is a full development box: you bring
  the SDK toolchains and offline package caches so the code can be modified and rebuilt with no
  internet at all.

## Stack

| Component | Technology |
|---|---|
| API | .NET 10 ASP.NET Core (self-contained for deploy) |
| Frontend | Angular 21 (static files) |
| Database | Oracle XE 21c (SQL Server, MySQL, PostgreSQL also supported by the API) |
| Auth | Active Directory / OpenLDAP |
| Web Server | nginx (portable) |

> The Infrastructure layer bundles managed drivers for Oracle, SQL Server, MySQL, and PostgreSQL.
> No native database client needs to be installed on the target — choose your DB via the connection string.

---

# Part A — Deploy a Pre-Built Release

## Step 1 — Build on an Internet-Connected Machine

### .NET API (self-contained publish)

Run from the repo root. Self-contained bundles the .NET runtime — no SDK or runtime needed on the target.

**Windows target:**
```powershell
dotnet publish src/DotNetDBTasks.API/DotNetDBTasks.API.csproj `
  -c Release `
  -r win-x64 `
  --self-contained `
  -o ./api-publish
```

**Linux target:**
```bash
dotnet publish src/DotNetDBTasks.API/DotNetDBTasks.API.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -o ./api-publish
```

> `Oracle.ManagedDataAccess.Core` (and the MySQL/PostgreSQL/SQL Server drivers) are fully managed
> and are bundled into the publish output. No separate database client installation is needed on the target.

### Angular Frontend

Run from the `client/` directory:

```powershell
npm ci
npm run build:prod
```

Output: `client/dist/dotnet-db-tasks-client/browser/`

---

## Step 2 — What to Take

| Item | Source | Notes |
|---|---|---|
| `api-publish/` | Built in Step 1 | Entire folder |
| `dist/dotnet-db-tasks-client/browser/` | Built in Step 1 | Entire folder |
| Oracle XE 21c installer | [oracle.com](https://www.oracle.com/database/technologies/xe-downloads.html) | ~1.5 GB, download before leaving |
| nginx portable zip | [nginx.org/en/download.html](https://nginx.org/en/download.html) | Windows: `nginx/Windows-x.x.x`, no install needed |
| `nginx.conf` | See Step 4 | Custom config for this app |
| `appsettings.json` | `src/DotNetDBTasks.API/` | Edit before going — see Step 3 |
| `docker/ldap/bootstrap.ldif` | Repo | Only needed if setting up a fresh OpenLDAP server |

---

## Step 3 — Edit `appsettings.json` Before Leaving

Update these values to match the isolated environment. Edit the copy inside `api-publish/appsettings.json`.

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "User Id=system;Password=YOUR_ORACLE_PASS;Data Source=localhost:1521/XEPDB1"
  },
  "Jwt": {
    "Secret": "CHANGE_THIS_TO_A_SECURE_SECRET_KEY_AT_LEAST_32_CHARS_LONG_12345"
  },
  "Encryption": {
    "Key": "YOUR_BASE64_ENCRYPTION_KEY"
  },
  "Ldap": {
    "Host": "YOUR_AD_OR_LDAP_SERVER_IP",
    "Port": 389,
    "BaseDn": "dc=yourdomain,dc=local",
    "UsersDn": "ou=users,dc=yourdomain,dc=local",
    "AdminDn": "cn=admin,dc=yourdomain,dc=local",
    "AdminPassword": "YOUR_LDAP_ADMIN_PASSWORD"
  },
  "Cors": {
    "AllowedOrigins": ["http://YOUR_SERVER_IP_OR_HOSTNAME"]
  }
}
```

> `Encryption.Key` must be a base64-encoded 32-byte key (`openssl rand -base64 32`). It encrypts stored
> database credentials at rest — **keep it consistent across rebuilds**, or previously stored credentials
> become unreadable.
>
> If the isolated network has **Active Directory**, the existing `sAMAccountName` / `department` attributes
> in the default config are already correct for AD. Just point `Ldap.Host` at the AD server. For OpenLDAP,
> use `uid` / `departmentNumber` (see `docker-compose.yml` for a working example).

---

## Step 4 — nginx Configuration

The Angular production build uses `/api` as a relative URL, so nginx must proxy `/api` requests to the .NET process.

Create this as `conf/nginx.conf` inside your nginx folder:

```nginx
worker_processes 1;

events {
    worker_connections 1024;
}

http {
    include mime.types;
    default_type application/octet-stream;
    sendfile on;
    keepalive_timeout 65;

    server {
        listen 80;

        root C:/deploy/client;
        index index.html;

        location / {
            try_files $uri $uri/ /index.html;
        }

        location /api {
            proxy_pass http://localhost:5000;
            proxy_http_version 1.1;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
        }
    }
}
```

Adjust `root` to wherever you copied the Angular `browser/` folder.

---

## Step 5 — Setup on the Isolated Machine

### 1. Install Oracle XE 21c

Run the Oracle XE installer. After installation, the default service is available at `localhost:1521/XEPDB1`.

### 2. Deploy files

Suggested layout:
```
C:\deploy\
  api\          ← contents of api-publish/
  client\       ← contents of dist/.../browser/
  nginx\        ← extracted nginx portable zip
    conf\
      nginx.conf
```

### 3. Start the API

**Run directly (for testing):**
```powershell
C:\deploy\api\DotNetDBTasks.API.exe
```

**Register as a Windows Service (for production):**
```powershell
sc.exe create DotNetDBTasks binPath="C:\deploy\api\DotNetDBTasks.API.exe"
sc.exe start DotNetDBTasks
```

The API listens on port `5000` by default. To change it, add to `appsettings.json`:
```json
"Urls": "http://localhost:5000"
```

### 4. Start nginx

```powershell
cd C:\deploy\nginx
.\nginx.exe
```

To stop:
```powershell
.\nginx.exe -s stop
```

### 5. Verify

- Frontend: `http://localhost` or `http://YOUR_SERVER_IP`
- API health: `http://localhost/api/...` (proxied through nginx)
- API direct: `http://localhost:5000/swagger` (for debugging only)

---

## Startup Order

1. Oracle XE (database must be up first)
2. `DotNetDBTasks.API.exe` (waits for DB connection)
3. nginx (serves frontend and proxies API)

---

# Part B — Edit and Rebuild Offline

A run-only deployment (Part A) does **not** let you change the code. To edit and rebuild on a machine
that never touches the internet, you must bring the full toolchains plus the package caches the build
restores from. Prepare everything below on an internet-connected machine, then carry it across.

## B1 — Toolchains to Install

| Item | Why | Source |
|---|---|---|
| **.NET 10 SDK** (offline installer, win-x64) | Rebuild the API (`dotnet build`/`publish`). The runtime alone is not enough. | dotnet.microsoft.com/download/dotnet/10.0 |
| **Node.js 24.x** (Windows installer) | Rebuild the Angular frontend. Match the version used to build (currently `v24.x`, npm `10.x`). | nodejs.org |
| **Editor (optional)** | VS Code offline build + `.vsix` extensions (C# Dev Kit, Angular), **or** Visual Studio 2022/2026 with the ASP.NET + .NET 10 workloads. You can also build entirely from the CLI with any text editor. | code.visualstudio.com / visualstudio.com |

> Use the **offline / full** installers, not the small web bootstrappers — those download during install.

## B2 — NuGet Packages (offline)

The build restores ~20 packages (EF Core 10, the four DB drivers, MediatR, AutoMapper, FluentValidation,
Serilog, Swashbuckle, BCrypt, Novell LDAP, JwtBearer, …) plus transitive dependencies from nuget.org.
Restore will fail offline unless these are present locally.

**Simplest reliable method — carry the whole global package cache.** On the online machine:

```powershell
# Populate the global cache with this solution's full dependency closure
dotnet restore DotNetDBTasks.sln

# The cache lives here:
#   %USERPROFILE%\.nuget\packages
```

Copy `%USERPROFILE%\.nuget\packages` to the **same path** on the air-gapped machine. Restore then finds
everything locally and never hits the network.

**Alternative — a self-contained folder feed** (keeps the repo portable). Create a flat folder of `.nupkg`
files and point a `nuget.config` (next to `DotNetDBTasks.sln`) at it:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="offline" value="./offline-nuget" />
  </packageSources>
</configuration>
```

Then commit/copy the `offline-nuget` folder alongside the source. Restore uses only that feed.

## B3 — npm Packages (offline)

The repo's `client/node_modules/` is already populated, **but it contains native, platform-specific binaries**
(`@rollup/rollup-win32-x64-msvc`, `@napi-rs/nice-win32-x64-msvc`, `lmdb`, …). How you carry npm depends on
whether the target OS/arch matches.

- **Same OS + arch (Windows x64 → Windows x64):** copy the entire `client/` folder **including
  `node_modules/`**. You can rebuild immediately with `npm run build:prod` — no network, no reinstall.

- **Different OS/arch, or you want a clean install:** bring an **npm offline cache** instead. On the
  online machine:

  ```powershell
  cd client
  npm ci --cache ./npm-cache         # downloads every package into ./npm-cache
  ```

  Copy `client/` (you can omit `node_modules`) **plus** `npm-cache`. On the target:

  ```powershell
  cd client
  npm ci --offline --cache ./npm-cache
  ```

## B4 — What to Take (Part B)

| Item | Notes |
|---|---|
| Full repo **source tree** | The actual code (git bundle or plain copy). |
| .NET 10 SDK offline installer | win-x64 |
| `%USERPROFILE%\.nuget\packages` (copied) **or** `offline-nuget` feed | Offline NuGet restore |
| Node.js 24.x installer | Match the build version |
| `client/node_modules` (same OS/arch) **or** `npm-cache` | Offline npm |
| Editor + `.vsix` extensions | Optional |
| Everything from Part A's Step 2 | DB, nginx, etc. for actually running what you build |

## B5 — Verify Offline Before You Disconnect

Prove the caches are complete by simulating offline on the prep machine (disable its network adapter, then):

```powershell
# API — clean restore + build with no network
dotnet build DotNetDBTasks.sln -c Release

# Frontend — clean install from cache only
cd client
npm ci --offline --cache ./npm-cache   # or: rebuild against the copied node_modules
npm run build:prod
```

If both succeed with the network off, the air-gapped machine has everything it needs.

## B6 — Rebuild Loop on the Air-Gapped Machine

```powershell
# After editing API code:
dotnet build DotNetDBTasks.sln -c Release
# or produce a fresh self-contained deploy:
dotnet publish src/DotNetDBTasks.API/DotNetDBTasks.API.csproj -c Release -r win-x64 --self-contained -o ./api-publish

# After editing frontend code:
cd client
npm run build:prod
```

Then redeploy the outputs as in Part A, Step 5.

---

## Troubleshooting

| Problem | Check |
|---|---|
| API fails to start | Database running? Connection string correct in `appsettings.json`? |
| Login fails | LDAP/AD host reachable from the server? Credentials and attribute mapping correct? |
| Stored DB credentials unreadable | `Encryption.Key` changed between builds — it must stay constant |
| Frontend shows blank / 404 | nginx `root` path pointing to the correct `browser/` folder? |
| `/api` calls return 502 | Is the API process actually running on port 5000? |
| CORS errors | `Cors.AllowedOrigins` in `appsettings.json` matches the exact URL you're using |
| `dotnet restore` fails offline | Global cache not copied to `%USERPROFILE%\.nuget\packages`, or `nuget.config` not pointing at the offline feed |
| `npm` tries to reach the network | Missing `--offline`, wrong `--cache` path, or `node_modules` was built for a different OS/arch |
