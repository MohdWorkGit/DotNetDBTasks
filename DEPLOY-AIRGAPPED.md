# Air-Gapped Deployment Guide (No Docker, No Internet)

This guide covers deploying the full stack to an isolated network with no internet access.

## Stack

| Component | Technology |
|---|---|
| API | .NET 10 ASP.NET Core (self-contained) |
| Frontend | Angular (static files) |
| Database | Oracle XE 21c |
| Auth | Active Directory / OpenLDAP |
| Web Server | nginx (portable) |

---

## Step 1 — Build on Internet-Connected Machine

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

> `Oracle.ManagedDataAccess.Core` is a fully managed driver and is bundled into the publish output. No separate Oracle client installation is needed on the target machine.

### Angular Frontend

Run from the `client/` directory:

```powershell
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

> If the isolated network has **Active Directory**, the existing `sAMAccountName` / `department` attributes in the default config are already correct for AD. Just point `Ldap.Host` at the AD server.

---

## Step 4 — nginx Configuration

The Angular production build uses `/api` as a relative URL, so nginx must proxy `/api` requests to the .NET process.

Create this as `conf/nginx.conf` inside your nginx folder:

```nginx
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

The API listens on port `5000` by default. To change it, set the environment variable or add to `appsettings.json`:
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

## Troubleshooting

| Problem | Check |
|---|---|
| API fails to start | Oracle XE running? Connection string correct in `appsettings.json`? |
| Login fails | LDAP/AD host reachable from the server? Credentials correct? |
| Frontend shows blank / 404 | nginx `root` path pointing to the correct `browser/` folder? |
| `/api` calls return 502 | Is the API process actually running on port 5000? |
| CORS errors | `Cors.AllowedOrigins` in `appsettings.json` matches the exact URL you're using |
