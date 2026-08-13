---
name: verify
description: Build, launch and drive Bayan (API + Angular client) to verify changes end-to-end.
---

# Verify Bayan

## Launch
- API: `dotnet run --no-launch-profile --urls http://localhost:60187` in `src/Bayan.API` (background). Oracle reachable; migrations/schema repair auto-apply. If port 60187 is busy, kill the owner (`Get-NetTCPConnection -LocalPort 60187`) — it's usually a stale API with old code.
- Client: `npx ng serve --port 4200` in `client/` (often already running; vite hot-reloads edits, but the changed lazy chunk only compiles on first page hit — first automated run may hit timeouts, just retry).
- Logins: admin/Admin@123, auditor/Auditor@123 (`user` was renamed by past e2e runs).

## API-level probes
Login: `POST /api/auth/login {"username","password"}` → token is in the **`accessToken`** field. Pass as `Authorization: Bearer`.

## UI driving (Playwright)
- Install in the session scratchpad: `npm i playwright && npx playwright install chromium` (chromium usually cached).
- Login form: `input[formcontrolname=username]` / `password`, then `button[type=submit]`; buttons findable via literal `[mattooltip="..."]`.
- `mat-accordion` pages (e.g. `/admin/scheduled-tasks/:id/runs`) allow ONE expanded panel — expanding another collapses the first and makes its links unclickable (hidden). Do per-panel work while that panel is open.
- Downloads: `page.waitForEvent('download')` + click; `download.saveAs(...)`.
- Windows Python can't open `/c/...` Git Bash paths — use `C:/...`.

## Gotchas (see also auto-memory dev-test-setup-and-gotchas)
- Zoneless CD: every subscribe callback needs `cdr.detectChanges()` or the view never updates.
- Scheduled-task export files live in `ScheduledTask.OutputFolder` (+ optional `ArchiveFolder` copy); run history only records bare file names inside `ItemResultsJson`.
