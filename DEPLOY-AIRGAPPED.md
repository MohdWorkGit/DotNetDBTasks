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

### QueryRunner console tool (optional)

`tools/DotNetDBTasks.QueryRunner` is a standalone console app that runs SELECT queries from a JSON
config and exports the results (Excel/CSV/JSON, with incremental checkpoints) — made to be driven by
Windows Task Scheduler with no API, app database, or login. Publish it self-contained too if the
target has no .NET runtime:

```powershell
dotnet publish tools/DotNetDBTasks.QueryRunner `
  -c Release `
  -r win-x64 `
  --self-contained `
  -o ./queryrunner-publish
```

See [tools/DotNetDBTasks.QueryRunner/README.md](tools/DotNetDBTasks.QueryRunner/README.md) for
configuration (`appsettings.json` next to the exe, or a config path per scheduled job) and the
Task Scheduler setup.

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
| `queryrunner-publish/` (optional) | Built in Step 1 | Only if using the standalone QueryRunner with Task Scheduler |
| LibreOffice installer (optional) | [libreoffice.org](https://www.libreoffice.org/download/download-libreoffice/) | ~350 MB. Only for full-fidelity Word-template PDF export — see below |
| `docker/ldap/bootstrap.ldif` | Repo | Only needed if setting up a fresh OpenLDAP server |

> **PDF export engine.** Exports that use a Word template are rendered to `.docx` first, then
> converted to PDF by whichever engine the host offers: **LibreOffice** (headless, preferred) or
> **Microsoft Word** via COM automation if Office is installed. With neither present the app does
> *not* fail — it falls back to a built-in PDF layout, which ignores the Word template's styling.
> If template-faithful PDFs matter, **carry a LibreOffice installer across now**; you cannot
> download one later. Control the choice with `Export:PdfEngine` (`auto` | `libreoffice` | `word` |
> `builtin`) and, for a non-standard install location, `Export:LibreOfficePath`.

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

> Scheduled export tasks run inside this process, so it must stay running for schedules to fire —
> a service (not a console window someone closes) is the right choice when you use them. The service
> account needs **write access to every task's `OutputFolder` and `ArchiveFolder`**.
>
> If you copied the QueryRunner tool, place it anywhere (e.g. `C:\deploy\queryrunner\`), edit its
> `appsettings.json`, and wire it to Windows Task Scheduler per its README — it runs independently
> of the API and nginx.

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

> **Fast path — one command does all of it.** The sections below explain each piece, but
> `scripts/prepare-offline-bundle.ps1` automates the whole job into a single `offline-bundle/`
> folder (NuGet closure, npm cache, self-contained API + QueryRunner, Angular build, and the
> SDK/Node installers):
>
> ```powershell
> ./scripts/prepare-offline-bundle.ps1 -Clean -IncludeBuild -IncludeInstallers
> ```
>
> Copy that folder across and follow its generated `MANIFEST.txt`. Read on if you'd rather
> assemble the pieces by hand or need to understand what the script produces.

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

# The QueryRunner tool is NOT in the solution — restore it separately or its
# packages will be missing from the cache (scripts/prepare-offline-bundle.ps1
# already does both restores for you):
dotnet restore tools/DotNetDBTasks.QueryRunner

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

> **If you prepared a bundle before Arabic/RTL support was added, regenerate it.** Localization
> introduced two new runtime dependencies — `@jsverse/transloco` and `@fontsource/cairo` — so an
> older `npm-cache` will fail `npm ci --offline` with a missing-package error. Re-run
> `scripts/prepare-offline-bundle.ps1 -Clean -IncludeBuild` on the online machine.
>
> Both are deliberately npm packages rather than CDN links: the Arabic font must ship inside the
> bundle, exactly like the existing `@fontsource/inter` and `@fontsource/roboto` imports in
> `client/src/styles.scss`. Nothing in the UI fetches a font or a translation file from the
> internet — the `ar.json` / `en.json` catalogs are served as static assets from the app's own
> `assets/i18n/` folder.

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

# QueryRunner tool (not in the solution)
dotnet build tools/DotNetDBTasks.QueryRunner -c Release

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

# After editing the QueryRunner tool:
dotnet publish tools/DotNetDBTasks.QueryRunner -c Release -r win-x64 --self-contained -o ./queryrunner-publish

# After editing frontend code:
cd client
npm run build:prod
```

Then redeploy the outputs as in Part A, Step 5.

---

# Part C — Host on IIS (Single Site, with Windows SSO)

An alternative to nginx. In this model **one IIS site serves everything**: the API (`Program.cs`
calls `UseStaticFiles` + `MapFallbackToFile("index.html")`) hosts the Angular build from its own
`wwwroot`, so `/api/*` hits the controllers and every other path returns the SPA. No reverse proxy,
no CORS (same origin), and Windows SSO works through IIS.

## C1 — Server Prerequisites

1. Enable the **IIS** role, including the **Windows Authentication** feature (needed for SSO).
2. Install the **.NET 10 ASP.NET Core Hosting Bundle** (`dotnet-hosting-10.0.x-win.exe`). This
   provides the ASP.NET Core Module (ANCM) that IIS uses to run the app — required even for a
   self-contained publish. Then restart IIS: `net stop was /y & net start w3svc`.

## C2 — Build and Assemble the Publish Folder

```powershell
# Angular
cd client; npm run build:prod        # -> client/dist/dotnet-db-tasks-client/browser/

# API (self-contained)
dotnet publish src/DotNetDBTasks.API/DotNetDBTasks.API.csproj -c Release -r win-x64 --self-contained -o ./api-publish

# Merge the SPA into the API's wwwroot
New-Item -ItemType Directory -Force .\api-publish\wwwroot | Out-Null
Copy-Item .\client\dist\dotnet-db-tasks-client\browser\* .\api-publish\wwwroot\ -Recurse -Force
```

`dotnet publish` emits a `web.config` based on the one in `src/DotNetDBTasks.API/`, so the published
site already carries the Windows/Anonymous authentication block. Edit `api-publish\appsettings.json`
as in Part A, Step 3 (the `Cors:AllowedOrigins` value is unused here — same origin).

## C3 — Create the IIS Site

```powershell
Import-Module WebAdministration
New-WebAppPool -Name "DotNetDBTasks"
# No Managed Code: ANCM runs the .NET process, not the IIS CLR.
Set-ItemProperty IIS:\AppPools\DotNetDBTasks -Name managedRuntimeVersion -Value ""
New-Website -Name "DotNetDBTasks" -Port 80 -PhysicalPath "C:\inetpub\DotNetDBTasks" -ApplicationPool "DotNetDBTasks"
# The app writes logs\ and reads wwwroot — grant the pool identity write access.
icacls "C:\inetpub\DotNetDBTasks" /grant "IIS AppPool\DotNetDBTasks:(OI)(CI)M" /T
```

(Copy `api-publish\` to `C:\inetpub\DotNetDBTasks` first.)

## C3.5 — Keep the Background Scheduler Alive

Scheduled export tasks run inside the app's worker process. By default IIS stops an idle
app pool after 20 minutes and only restarts the app on the next HTTP request — so overnight
schedules would silently never fire. Configure the pool/site to run permanently:

1. Install the IIS **Application Initialization** feature (Server Manager → Web Server →
   Application Development, or `dism /online /enable-feature /featurename:IIS-ApplicationInit`).
2. Configure the pool and site:
   ```powershell
   Import-Module WebAdministration
   # Never stop when idle; start with Windows instead of on first request.
   Set-ItemProperty IIS:\AppPools\DotNetDBTasks -Name processModel.idleTimeout -Value "00:00:00"
   Set-ItemProperty IIS:\AppPools\DotNetDBTasks -Name startMode -Value AlwaysRunning
   # Warm the app immediately after any recycle/restart, without waiting for a visitor.
   Set-ItemProperty "IIS:\Sites\DotNetDBTasks" -Name applicationDefaults.preloadEnabled -Value $true
   ```
3. Optional: disable the daily scheduled recycle, or move it to a quiet hour
   (`Set-ItemProperty IIS:\AppPools\DotNetDBTasks -Name recycling.periodicRestart.time -Value "00:00:00"`).
   A recycle during a running export fails that run; it is retried-safe (incremental
   checkpoints only advance on success) but the run shows as failed.
4. Grant the pool identity **write access to every scheduled task output folder** (same
   `icacls` pattern as C3) — the folders in `OutputFolder` **and** `ArchiveFolder` of your
   scheduled tasks.

## C4 — Windows SSO

> **SSO is off by default.** Set `"Auth": { "EnableSso": true }` in `appsettings.json` to enable
> the endpoint; while disabled it returns 404 and users sign in with their AD username/password
> on the login form (validated by a live LDAP bind — no IIS Windows-auth setup needed at all).

The app exposes `GET /api/auth/sso`: a domain-joined browser sends the user's Kerberos/NTLM ticket
automatically, the endpoint auto-provisions/syncs the user from AD via LDAP, then issues the app's
own JWT. Every later `/api/*` call uses that JWT (Bearer). The endpoint is authorized with
`IISDefaults.AuthenticationScheme`, so **IIS** performs Windows auth (correct for in-process hosting).

1. **Unlock the auth sections** (they are locked at the server level by default, else IIS returns
   HTTP 500.19). Run once per server, as admin:
   ```powershell
   & "$env:windir\system32\inetsrv\appcmd.exe" unlock config /section:windowsAuthentication
   & "$env:windir\system32\inetsrv\appcmd.exe" unlock config /section:anonymousAuthentication
   ```
   The shipped `web.config` then enables **both** Anonymous (keeps the SPA + JWT endpoints open) and
   Windows (lets `/api/auth/sso` challenge on demand). Or skip the unlock and enable both in IIS
   Manager → the site → *Authentication*.
2. **Silent login prerequisites:**
   - Users reach the site by **hostname** (e.g. `http://dbtasks.corp.local`), and that host is in the
     browser's **Local Intranet** zone — otherwise the browser prompts instead of logging in silently.
   - If the app pool runs under a **custom domain account**, register an SPN:
     `setspn -S HTTP/dbtasks.corp.local DOMAIN\svc-account`. Under `ApplicationPoolIdentity`/`NetworkService`
     the machine account already covers the host's own name.
   - Keep `Ldap:*` valid — SSO supplies the username; the profile lookup still goes through AD.
   - Prefer HTTPS in production.

## C5 — Verify

- `http://SERVER/` loads the SPA; refreshing a deep link (e.g. `/admin/queries`) still works (fallback).
- `http://SERVER/api/...` responds; `http://SERVER/swagger` shows the API docs.
- `http://SERVER/api/auth/sso` from a domain-joined machine returns a token without prompting.
- ANCM startup failures surface in **Event Viewer → Windows Logs → Application**; app logs are in
  `logs\log-*.txt`.

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
| IIS: HTTP 500.19 (config locked) | `windowsAuthentication`/`anonymousAuthentication` not unlocked — run the `appcmd unlock` commands in Part C4, or set auth in IIS Manager instead |
| IIS: HTTP 500.30 / 502.5 on start | Hosting Bundle not installed, app pool not set to *No Managed Code*, or app crashed on startup — see Event Viewer → Application and `logs\` |
| IIS: SSO prompts for credentials | Site reached by IP not hostname, host not in the browser's Local Intranet zone, or missing SPN for a custom app-pool account |
