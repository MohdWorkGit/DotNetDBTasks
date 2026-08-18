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
dotnet publish src/Bayan.API/Bayan.API.csproj `
  -c Release `
  -r win-x64 `
  --self-contained `
  -o ./api-publish
```

**Linux target:**
```bash
dotnet publish src/Bayan.API/Bayan.API.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -o ./api-publish
```

> `Oracle.ManagedDataAccess.Core` (and the MySQL/PostgreSQL/SQL Server drivers) are fully managed
> and are bundled into the publish output. No separate database client installation is needed on the target.

### QueryRunner console tool (optional)

`tools/Bayan.QueryRunner` is a standalone console app that runs SELECT queries from a JSON
config and exports the results (Excel/CSV/JSON, with incremental checkpoints) — made to be driven by
Windows Task Scheduler with no API, app database, or login. Publish it self-contained too if the
target has no .NET runtime:

```powershell
dotnet publish tools/Bayan.QueryRunner `
  -c Release `
  -r win-x64 `
  --self-contained `
  -o ./queryrunner-publish
```

See [tools/Bayan.QueryRunner/README.md](tools/Bayan.QueryRunner/README.md) for
configuration (`appsettings.json` next to the exe, or a config path per scheduled job) and the
Task Scheduler setup.

### Angular Frontend

Run from the `client/` directory:

```powershell
npm ci
npm run build:prod
```

Output: `client/dist/bayan-client/browser/`

---

## Step 2 — What to Take

| Item | Source | Notes |
|---|---|---|
| `api-publish/` | Built in Step 1 | Entire folder |
| `dist/bayan-client/browser/` | Built in Step 1 | Entire folder |
| Oracle XE 21c installer | [oracle.com](https://www.oracle.com/database/technologies/xe-downloads.html) | ~1.5 GB, download before leaving |
| nginx portable zip | [nginx.org/en/download.html](https://nginx.org/en/download.html) | Windows: `nginx/Windows-x.x.x`, no install needed |
| `nginx.conf` | See Step 4 | Custom config for this app |
| `appsettings.json` | `src/Bayan.API/` | Edit before going — see Step 3 |
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
> use `uid` / `departmentNumber` (see `docker-compose.yml` for a working example). The department attribute
> only drives the **AD Users** page — browsing and importing accounts by department. Permissions come from
> the application's own user groups, so a wrong or missing department costs nobody their access.

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
C:\deploy\api\Bayan.API.exe
```

**Register as a Windows Service (for production):**
```powershell
sc.exe create Bayan binPath="C:\deploy\api\Bayan.API.exe"
sc.exe start Bayan
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
2. `Bayan.API.exe` (waits for DB connection)
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
dotnet restore Bayan.sln

# The QueryRunner tool is NOT in the solution — restore it separately or its
# packages will be missing from the cache (scripts/prepare-offline-bundle.ps1
# already does both restores for you):
dotnet restore tools/Bayan.QueryRunner

# The cache lives here:
#   %USERPROFILE%\.nuget\packages
```

Copy `%USERPROFILE%\.nuget\packages` to the **same path** on the air-gapped machine. Restore then finds
everything locally and never hits the network.

**Alternative — a self-contained folder feed** (keeps the repo portable). Create a flat folder of `.nupkg`
files and point a `nuget.config` (next to `Bayan.sln`) at it:

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
dotnet build Bayan.sln -c Release

# QueryRunner tool (not in the solution)
dotnet build tools/Bayan.QueryRunner -c Release

# Frontend — clean install from cache only
cd client
npm ci --offline --cache ./npm-cache   # or: rebuild against the copied node_modules
npm run build:prod
```

If both succeed with the network off, the air-gapped machine has everything it needs.

## B6 — Rebuild Loop on the Air-Gapped Machine

```powershell
# After editing API code:
dotnet build Bayan.sln -c Release
# or produce a fresh self-contained deploy:
dotnet publish src/Bayan.API/Bayan.API.csproj -c Release -r win-x64 --self-contained -o ./api-publish

# After editing the QueryRunner tool:
dotnet publish tools/Bayan.QueryRunner -c Release -r win-x64 --self-contained -o ./queryrunner-publish

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

> **Fast path — one command does all of it.** `scripts/deploy-iis.ps1` performs every step in
> Part C: it builds the client, publishes the API, merges the SPA into `wwwroot`, and creates or
> updates the app pool and site with all of the settings below. Run it elevated, on the server:
>
> ```powershell
> ./scripts/deploy-iis.ps1 -Hostname bayan.corp.local -EnableSso
> ```
>
> Re-running it is an upgrade, not a reinstall — existing bindings, `appsettings.json`, `logs\`
> and `cache\` are preserved. Read on if you would rather do it by hand, or need to understand
> what it configures and why.

## C0 — Why This App Needs More Than the Default App Pool

Bayan runs three background workers **inside the IIS worker process**: the scheduled-export
scheduler, the async query-job worker, and result-cache maintenance. Their state — the job
store, both work queues, the cancel registry — lives in that process's memory
(`src/Bayan.Infrastructure/DependencyInjection.cs`). Three consequences drive everything in C3
and C3.5, and none of them are optional:

- **The pool must run exactly one worker process.** A web garden (`maxProcesses > 1`) starts a
  second scheduler that fires every due task a second time, writing duplicate export files. It
  also splits the job store, so a browser polling for its query result can be answered by the
  process that never ran it.
- **Recycles must not overlap.** On startup the job store deletes everything in its spill
  directory to clear orphans left by a crash. During an overlapped recycle the incoming process
  does that while the outgoing one is still streaming those files to someone's browser.
- **The pool must never stop when idle.** The scheduler polls in-process. An idled pool means
  the export due at 02:00 simply does not happen, and nothing reports that it didn't.

## C1 — Server Prerequisites

All four are required. The site will not start without them — none is an optimisation.

| # | Prerequisite | Why | Install |
|---|---|---|---|
| 1 | **IIS role** with **Windows Authentication** | SSO. Without it `/api/auth/sso` cannot challenge. | `Web-Windows-Auth` |
| 2 | **Application Initialization** | The shipped `web.config` contains an `<applicationInitialization>` element. If the feature is absent IIS cannot parse it and answers **HTTP 500.19** — the site does not start at all. It is also what makes `preloadEnabled` warm the app so the background workers start without a visitor. | `dism /online /enable-feature /featurename:IIS-ApplicationInit` |
| 3 | **IIS Management Scripts and Tools** | Provides the `WebAdministration` PowerShell module that C3/C3.5 and `scripts/deploy-iis.ps1` use. | `Web-Scripting-Tools` |
| 4 | **.NET 10 ASP.NET Core Hosting Bundle** (`dotnet-hosting-10.0.x-win.exe`) | The ASP.NET Core Module (ANCM) that IIS uses to run the app — **required even for a self-contained publish**. | Run the installer |

```powershell
Install-WindowsFeature Web-Server, Web-Windows-Auth, Web-AppInit, Web-Scripting-Tools
# then the Hosting Bundle installer, then:
net stop was /y & net start w3svc
```

> **Air-gapped note.** `scripts/prepare-offline-bundle.ps1 -IncludeInstallers` carries the
> Hosting Bundle into `installers/`, but the four IIS features above come from the **Windows
> installation media**, not from the bundle. On a server with no internet, `Install-WindowsFeature`
> needs `-Source <path to \sources\sxs>` from the matching Windows Server ISO.

## C2 — Build and Assemble the Publish Folder

```powershell
# Angular
cd client; npm run build:prod        # -> client/dist/bayan-client/browser/

# API (self-contained)
dotnet publish src/Bayan.API/Bayan.API.csproj -c Release -r win-x64 --self-contained -o ./api-publish

# Merge the SPA into the API's wwwroot
New-Item -ItemType Directory -Force .\api-publish\wwwroot | Out-Null
Copy-Item .\client\dist\bayan-client\browser\* .\api-publish\wwwroot\ -Recurse -Force
```

`dotnet publish` emits a `web.config` based on the one in `src/Bayan.API/`, so the published
site already carries the Windows/Anonymous authentication block. Edit `api-publish\appsettings.json`
as in Part A, Step 3 (the `Cors:AllowedOrigins` value is unused here — same origin).

## C3 — Create the IIS Site

```powershell
Import-Module WebAdministration
New-WebAppPool -Name "Bayan"
# No Managed Code: ANCM runs the .NET process, not the IIS CLR.
Set-ItemProperty IIS:\AppPools\Bayan -Name managedRuntimeVersion -Value ""
# Reach the site by hostname, not IP — silent SSO depends on it (see C4).
New-Website -Name "Bayan" -Port 80 -HostHeader "bayan.corp.local" `
            -PhysicalPath "C:\inetpub\Bayan" -ApplicationPool "Bayan"
# The app writes logs\ and spills large query results to cache\ — grant the pool identity
# write access.
icacls "C:\inetpub\Bayan" /grant "IIS AppPool\Bayan:(OI)(CI)M" /T
```

(Copy `api-publish\` to `C:\inetpub\Bayan` first.)

**Then apply the app pool settings in C3.5 — they are not optional for this app.**

### Choosing the pool identity

| | `ApplicationPoolIdentity` (default) | Domain service account |
|---|---|---|
| SSO setup | Nothing to do — the machine account already covers this host's own name | Requires `setspn -S HTTP/<hostname> DOMAIN\svc-bayan` |
| Scheduled exports to a **network share** | **Not possible** — output folders must be local paths | Works |
| Windows auth to Oracle | Not available | Available |
| Credential management | None | Password must be rotated in IIS when it changes in AD |

Start with `ApplicationPoolIdentity` unless a scheduled task needs to write to a UNC path.
To switch later:

```powershell
Set-ItemProperty IIS:\AppPools\Bayan -Name processModel.identityType -Value SpecificUser
Set-ItemProperty IIS:\AppPools\Bayan -Name processModel.userName -Value "CORP\svc-bayan"
Set-ItemProperty IIS:\AppPools\Bayan -Name processModel.password -Value "..."
```

## C3.5 — Keep the Background Workers Alive

Install the IIS **Application Initialization** feature first (Server Manager → Web Server →
Application Development, or `dism /online /enable-feature /featurename:IIS-ApplicationInit`).
Without it `preloadEnabled` does nothing and the app waits for a first visitor before its
workers start.

Then apply all of the following. See C0 for why each one matters:

```powershell
Import-Module WebAdministration

# Never stop when idle; start with Windows instead of on first request.
Set-ItemProperty IIS:\AppPools\Bayan -Name processModel.idleTimeout -Value "00:00:00"
Set-ItemProperty IIS:\AppPools\Bayan -Name startMode -Value AlwaysRunning

# Exactly one worker process. A web garden duplicates every scheduled export and
# splits the in-memory job store across processes.
Set-ItemProperty IIS:\AppPools\Bayan -Name processModel.maxProcesses -Value 1

# No overlapped recycle: the incoming process wipes the result spill directory on
# startup, which would pull files out from under the outgoing process's readers.
Set-ItemProperty IIS:\AppPools\Bayan -Name recycling.disallowOverlappingRotation -Value $true

# Turn off every automatic recycle trigger. Each is an unannounced restart that drops
# in-flight query jobs and every cached result.
Set-ItemProperty IIS:\AppPools\Bayan -Name recycling.periodicRestart.time -Value "00:00:00"
Set-ItemProperty IIS:\AppPools\Bayan -Name recycling.periodicRestart.requests -Value 0
Set-ItemProperty IIS:\AppPools\Bayan -Name recycling.periodicRestart.memory -Value 0
Set-ItemProperty IIS:\AppPools\Bayan -Name recycling.periodicRestart.privateMemory -Value 0
Clear-ItemProperty IIS:\AppPools\Bayan -Name recycling.periodicRestart.schedule

# Outermost rung of the shutdown ladder (see below).
Set-ItemProperty IIS:\AppPools\Bayan -Name processModel.shutdownTimeLimit -Value "00:02:00"

# Warm the app immediately after any restart, without waiting for a visitor.
Set-ItemProperty "IIS:\Sites\Bayan" -Name applicationDefaults.preloadEnabled -Value $true
```

### The shutdown ladder

A restart is not always avoidable — a deploy is one. What decides whether it costs you
anything is how much time the app gets to unwind. Given enough, a running export takes the
cancellation path that writes its final run status **and** flushes the incremental checkpoints
of the items that already finished; killed instead, it does neither and re-exports them next
time. Three timeouts gate that, and the innermost must be the smallest:

| Limit | Where | Value |
|---|---|---|
| `HostOptions.ShutdownTimeout` | `appsettings.json` → `Host:ShutdownTimeoutSeconds` | 90s |
| ANCM `shutdownTimeLimit` | `web.config` → `<aspNetCore>` | 100s |
| App pool `shutdownTimeLimit` | IIS, above | 120s |

ANCM's default is **10 seconds**, which is far too short — leave it and every recycle is a hard
kill regardless of what the app is configured to do. Raise all three together or none.

### Still required

- Grant the pool identity **write access to every scheduled task output folder** — the
  `OutputFolder` **and** `ArchiveFolder` of each task, using the same `icacls` pattern as C3.
  Note that `ApplicationPoolIdentity` **cannot write to remote UNC shares**: with it, every
  output folder must be a local path. Use a domain service account if you need network shares.
- If a run is interrupted anyway, the app now closes it out itself: on startup the scheduler
  marks any run still sitting at `Running` as failed, so the run history cannot accumulate rows
  that show as in-progress forever and refuse to cancel.

## C4 — Windows SSO

> **SSO is off by default.** Set `"Auth": { "EnableSso": true }` in `appsettings.json` to enable
> the endpoint; while disabled it returns 404 and users sign in with their AD username/password
> on the login form (validated by a live LDAP bind — no IIS Windows-auth setup needed at all).

The app exposes `GET /api/auth/sso`: a domain-joined browser sends the user's Kerberos/NTLM ticket
automatically, the endpoint auto-provisions/syncs the user from AD via LDAP, then issues the app's
own JWT. Every later `/api/*` call uses that JWT (Bearer). The endpoint is authorized with
`IISDefaults.AuthenticationScheme`, so **IIS** performs Windows auth (correct for in-process hosting).

**What the user sees.** The login page attempts this automatically as it loads, so on a
domain-joined machine people arrive already signed in and never see the form. When the attempt
fails — SSO switched off, no ticket on offer, a non-domain machine — the form appears instead
and nothing is reported to the user; the fallback is meant to be invisible. Two consequences
worth knowing:

- **Signing out does not bounce.** The client suppresses the automatic attempt for the rest of
  the browser session after a sign-out, so logging out cannot immediately sign you back in.
  Closing the browser clears that.
- **`ng serve` cannot test this.** In development the client calls the API cross-origin, where
  the Negotiate handshake does not happen. The attempt fails fast and the form appears — correct
  behaviour, but it means SSO is only genuinely testable in this IIS-hosted configuration.

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
   - Users reach the site by **hostname** (e.g. `http://bayan.corp.local`), and that host is in the
     browser's **Local Intranet** zone — otherwise the browser prompts instead of logging in silently.
   - If the app pool runs under a **custom domain account**, register an SPN:
     `setspn -S HTTP/bayan.corp.local DOMAIN\svc-account`. Under `ApplicationPoolIdentity`/`NetworkService`
     the machine account already covers the host's own name.
   - Keep `Ldap:*` valid — SSO supplies the username; the profile lookup still goes through AD.
   - Prefer HTTPS in production.

## C5 — Verify

Basics:

- `http://SERVER/` loads the SPA; refreshing a deep link (e.g. `/admin/queries`) still works (fallback).
- `http://SERVER/api/...` responds; `http://SERVER/swagger` shows the API docs.
- `http://SERVER/api/auth/sso` from a domain-joined machine returns a token without prompting,
  and opening the site itself signs you in with no form.
- ANCM startup failures surface in **Event Viewer → Windows Logs → Application**; app logs are in
  `logs\log-*.txt`.

The three that actually prove Part C worked — the ones worth doing before calling a
deployment finished:

1. **The workers survive idleness.** Leave the site completely untouched for 30+ minutes, then
   confirm a scheduled task still fires. This is the only real test that `idleTimeout` and
   `startMode` took effect; everything looks fine until the first quiet night otherwise.
2. **A long query completes.** Run one that takes longer than two minutes. It should finish and
   page normally: long work goes through `execute-async`, so the browser polls with short
   requests and no HTTP timeout applies to the query itself.
3. **A recycle behaves.** With a task set to run every 5 minutes, `Restart-WebAppPool Bayan`
   mid-run, then check that the interrupted run shows `Canceled` or `Failed` rather than sitting
   at `Running`, that the next occurrence fires within ~30s of the restart, that no duplicate
   output file appeared, and that the output file that did land is complete rather than
   truncated.

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
| IIS: login form appears on a domain machine | `Auth:EnableSso` still `false` (the endpoint 404s), or the user signed out earlier in this browser session — the automatic attempt is suppressed until the browser is closed |
| IIS: **every scheduled export runs twice** | Web garden. `processModel.maxProcesses` must be `1` — a second worker process runs a second scheduler (C0) |
| IIS: results vanish mid-page, "no longer available" | An overlapped recycle wiped the spill directory. Set `recycling.disallowOverlappingRotation` to `$true` and disable the periodic recycles (C3.5) |
| Scheduled runs stuck at "Running" | Left by a process killed before it could finish. The scheduler now closes these out at startup — if they persist, the app is not restarting cleanly; check the shutdown ladder in C3.5 |
| Overnight schedules never fire | `idleTimeout` not `00:00:00`, or `startMode` not `AlwaysRunning` |
| IIS: HTTP 500.19 naming `applicationInitialization` | The Application Initialization feature is not installed, so IIS cannot parse that element in `web.config` — install it (C1 #2). Not the same as the config-lock 500.19 above |
| `Import-Module WebAdministration` fails | IIS Management Scripts and Tools missing — `Install-WindowsFeature Web-Scripting-Tools` (C1 #3) |
| Scheduled export writes fail to a network share | `ApplicationPoolIdentity` cannot reach UNC paths — switch to a domain service account (C3) or use a local output folder |
