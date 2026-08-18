# API Reference

Every endpoint the API exposes, grouped by controller.

**How to read the Roles column.** Endpoints name a **permission**, not a role
(`[RequirePermission(Permissions.QueriesRun)]`), so the column names the capability required.
Which roles hold it is a runtime decision, edited on **Settings → Permissions**; the seeded
defaults are in [the README](../README.md#roles-and-permissions). `Any authenticated` means a
valid token and nothing more; `Anonymous` means no token at all.

**Authentication.** Everything is JWT Bearer unless the row says otherwise:
`Authorization: Bearer <accessToken>` from `POST /api/auth/login`.

**Beyond the attribute.** Rules enforced in handlers, which no route table can show:

- A role without `queries.run` never resolves query access, however a query is assigned to it —
  so granting a query to such a role is inert rather than quietly broken.
- **Exporting needs two grants.** `DynamicQuery.AllowedExportFormats` says which formats a query
  may be downloaded as, and the caller's role must hold the matching `queries.export*` capability.
  Either one empty means no download. Both default closed, so a new query and a new role start
  with export off.
- `AdminAccountGuard` stops a non-Admin from touching an administrator account, granting the
  Admin role, editing their own roles, or adding themselves to a user group.
- **Admin is pinned** to every permission and cannot be edited or deleted.
- The `directory.enabled` setting switches the whole `/api/admin/ldap` controller off (503).

---

## Authentication — `/api/auth`

No class-level `[Authorize]`; all three are anonymous.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /sso` | Anonymous | Windows SSO (Kerberos/NTLM). **404 unless `Auth:EnableSso=true`** in appsettings. Provisions the account from AD with the `User` role on first sign-in and re-syncs the profile on each one. The sign-in page calls this by itself on load, so a domain user never reaches the form; the 404 and 401 paths are the silent fallback to it. Requires `withCredentials` — the browser will not perform the handshake otherwise. |
| `POST /login` | Anonymous | Username and password. Handles **both** local accounts (BCrypt hash) and directory accounts (LDAP bind) — the account's `AuthSource` decides which. **Rate limited** per client IP (`Auth:Login:PermitLimit`, default 10 per 60s); 429 when exceeded. |
| `POST /refresh` | Anonymous | Exchanges a valid refresh token for a new access token. Body carries the expired access token and the refresh token. |

Both `login` and `refresh` return `{ accessToken, refreshToken, expiresAt, username, roles }`.
`expiresAt` is the access token's own expiry — the lifetime comes from the
`session.accessTokenMinutes` setting, not from a constant.

## Branding — `/api/branding`

Class: `[Authorize]` (any authenticated user).

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /` | Any authenticated | `{ hasLogo, fileName, updatedAt, hasFavicon, faviconFileName, faviconUpdatedAt, siteNameEn, siteNameAr }` — everything the shell needs, in one call. A null site name means none is set for that language. |
| `GET /logo` | **Anonymous** (widened) | The logo bytes, or 404 when none is set. Anonymous because an `<img src>` carries no token; clients cache-bust with `?v=<updatedAt>`. Served `immutable`. |
| `POST /logo` | `branding.manage` | Upload/replace. multipart `file`, ≤1 MB, `.png/.jpg/.jpeg/.webp`, magic bytes verified (SVG is refused). |
| `DELETE /logo` | `branding.manage` | Removes it; the banner falls back to the site name, then to the application name. |
| `GET /favicon` | **Anonymous** (widened) | The browser-tab icon, or 404 when none is set — browsers then use their own default. Anonymous because the icon is fetched for the sign-in page, before any token exists. Served `no-cache` rather than `immutable`, since the first paint has no `updatedAt` to bust with. |
| `POST /favicon` | `branding.manage` | Upload/replace. multipart `file`, ≤256 KB, `.png/.ico/.webp`, magic bytes verified (an ICO must be type 1, not a cursor). |
| `DELETE /favicon` | `branding.manage` | Removes it; browsers fall back to their own default. |
| `PUT /site-name` | `branding.manage` | `{ siteNameEn, siteNameAr }`, each ≤60 chars. Both are written together; a blank one clears that language back to the application name. |

The banner resolves what to show in order: logo, then the site name for the active language,
then the translated application name — so it is never blank. The two names are stored in
`SystemSettings` under `branding.siteNameEn` / `branding.siteNameAr`, but they are edited here
under `branding.manage`, not on the Settings page under `settings.manage`.

The tab icon is independent of that chain: it is a square uploaded on its own rather than the
logo scaled down, and its absence means the browser's default, not a fallback of ours. Both
images live in `SystemTemplates` under `branding-logo` and `branding-favicon`.

## Queries — `/api/admin/dynamicqueries`

Class: `[Authorize]`. Every action names its own permission — an action added without one is
reachable by any signed-in caller, so add one.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /` | `queries.view` | All queries. **SQL is blanked** without `queries.readSql`. |
| `GET /{id}` | `queries.view` | One query with its parameters. SQL blanked as above. |
| `POST /` | `queries.manage` | Creates a query and its parameters. |
| `PUT /{id}` | `queries.manage` | Updates a query. |
| `DELETE /{id}` | `queries.manage` | Deletes a query. |
| `POST /{id}/roles` | `access.manageQuery` | Assigns to roles. No seeded role but Admin holds this, so it starts closed. |
| `POST /{id}/user-groups` | `access.manageQuery` | Assigns to user groups.  |
| `POST /{id}/users` | `access.manageQuery` | Assigns to named users.  |
| `POST /{id}/word-template` | `queries.manage` | Uploads this query's Word export template. multipart `file`, ≤5 MB, must be a real `.docx`. |
| `GET /{id}/word-template` | `queries.manage` | Downloads it; 404 when none. |
| `DELETE /{id}/word-template` | `queries.manage` | Removes it; exports fall back to the default. |
| `POST /default-word-template` | `queries.manage` | Uploads the system-wide default template. |
| `GET /default-word-template` | `queries.manage` | Downloads the default, or the built-in starter `.docx` when none is stored. |
| `GET /default-word-template/info` | `queries.manage` | `{ fileName, isBuiltIn }`. |
| `DELETE /default-word-template` | `queries.manage` | Removes the custom default. |
| `GET /{id}/export` | `queries.transfer` | Exports one query as JSON. |
| `GET /export` | `queries.transfer` | Exports every query as one JSON backup. Carries no database credentials. |
| `POST /import` | `queries.transfer` | Restores from an export file. multipart `file`, ≤50 MB. Never overwrites — a name clash is imported as a copy. |
| `GET /logs` | `logs.view` | Execution logs, paged. Query: `queryId`, `userId`, `isSuccess`, `queryType`, `search`, `sortBy`, `sortDescending` (default true), `pageNumber` (1), `pageSize` (25, max 200). |
| `GET /logs/{id}/old-values` | `logs.view` | Before-change row snapshots for one log. Query: `pageNumber` (1), `pageSize` (100, max 500). |

## Query groups — `/api/admin/querygroups`

Class: `[Authorize]`. Creating and editing a group is a different capability from granting one,
which is what lets a role decide accessibility without being able to reorganise it.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /` | `queries.view` | All query groups. |
| `GET /{id}` | `queries.view` | One group. |
| `POST /` | `queryGroups.manage` | Creates a group. |
| `PUT /{id}` | `queryGroups.manage` | Renames/updates a group. |
| `DELETE /{id}` | `queryGroups.manage` | Deletes the group; its queries survive and become ungrouped. |
| `POST /{id}/roles` | `access.manageGroup` | Grants the group — and so every query in it — to roles. |
| `POST /{id}/user-groups` | `access.manageGroup` | Same, to user groups. |
| `POST /{id}/users` | `access.manageGroup` | Same, to named users. |

## User groups — `/api/admin/usergroups`

Class: `[Authorize]`. The application's own groups; nothing here touches Active Directory.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /` | `userGroups.view` | All groups with their members. Never gated — the access pages are built from this. |
| `GET /{id}` | `userGroups.view` | One group with its members. |
| `POST /` | `userGroups.manage` | Creates a group, optionally with starting members. |
| `PUT /{id}` | `userGroups.manage` | Renames a group or edits its description. |
| `DELETE /{id}` | `userGroups.manage` | Deletes the group and every grant made to it. Member accounts are untouched. |
| `PUT /{id}/members` | `userGroups.manage` | Replaces membership with exactly the users given. |

Whatever the matrix says, a non-Admin cannot add **their own** account to a group — that would
let them grant a query to a group and then walk into it.

## Users — `/api/admin/users`

Class: `[Authorize]`; `AdminAccountGuard` does the fencing in the handlers. There is **no**
delete endpoint; deactivate instead.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /` | `users.view` | All users with their roles. |
| `GET /{id}` | `users.view` | One user. |
| `POST /` | `users.manage` | Creates a local account. A non-Admin cannot grant the Admin role. |
| `PUT /{id}/username` | `users.manage` | Changes the username. Refused for directory accounts. |
| `PUT /{id}/password` | `users.manage` | Sets a new password. Refused for directory accounts. |
| `POST /{id}/reset-password` | `users.manage` | Generates a temporary password and **returns it in the response**. Refused for directory accounts. |
| `PUT /{id}/roles` | `users.manage` | Replaces role assignments. A non-Admin cannot edit their own roles or grant Admin. |
| `PUT /{id}/active` | `users.manage` | Activates or deactivates. |

## Roles and permissions — `/api/admin/roles`

Class: `[Authorize]`. The data behind Settings → Permissions.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /` | Any authenticated | `{ id, name, description }` for each role. Source for the role pickers; names are not secret. |
| `GET /permissions` | `roles.manage` | The whole matrix: every capability the system defines, and every role with the ones it holds. Admin reports the full set rather than its (empty) rows. |
| `PUT /{id}/permissions` | `roles.manage` | Replaces one role's permissions. Unknown names are ignored; **400 for Admin**, which is pinned. Audited as `roles.setPermissions` with what was granted and revoked. |
| `POST /` | `roles.manage` | Creates a role. The name must be unused. |
| `PUT /{id}` | `roles.manage` | Renames a role or edits its description. **400 for a seeded role** — its permissions are editable, its name is not. |
| `DELETE /{id}` | `roles.manage` | Deletes a custom role. **400 while any account still holds it**, and 400 for a seeded role. |

## Active Directory — `/api/admin/ldap`

Class: `[Authorize]`. **The whole controller returns 503 when the `directory.enabled` setting is
off** — that check runs before every action, including any added
later. Signing in is unaffected.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /search` | `directory.manage` | Searches the directory. Query: `term` (2 chars minimum, else 400). Flags who is already imported. |
| `GET /departments` | `directory.view` | Distinct department values in the directory. |
| `GET /departments/{department}/users` | `directory.manage` | Directory users in one department, with import status. |
| `POST /import/users` | `directory.manage` | Imports `{ usernames: [] }` as local accounts with `AuthSource=Ldap` and the `User` role. Reports imported / already present / not found / skipped-with-reason. |
| `POST /import/department` | `directory.manage` | Imports every directory user in `{ department }`. Same result shape. |
| `POST /revoke/{username}` | `directory.manage` | Deactivates an imported account. |
| `POST /restore/{username}` | `directory.manage` | Reactivates it. |
| `POST /sync` | `directory.manage` | Refreshes name, email and department for every imported account. Returns `{ synced, notFound }`. Does not change group membership or anyone's access. |
| `GET /imported` | `directory.view` | Imported directory accounts with their status. |

## Database connections — `/api/admin/databaseusers`

Class: `[Authorize]`.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /` | `databaseUsers.manage` | All stored connections. |
| `GET /{id}` | `databaseUsers.manage` | One connection. |
| `POST /` | `databaseUsers.manage` | Creates one. Credentials are encrypted at rest. |
| `PUT /{id}` | `databaseUsers.manage` | Updates one. |
| `DELETE /{id}` | `databaseUsers.manage` | Deletes one. |
| `POST /{id}/access` | `databaseUsers.manage` | Sets which roles may use this connection. |
| `POST /{id}/test-connection` | `databaseUsers.manage` | Opens a test connection with the stored credentials. |
| `GET /accessible` | **Any authenticated** (widened) | Connections the caller may use, for the query form's dropdown. Note the path sits under `/api/admin/` but is **not** Admin-only. |

## Scheduled tasks — `/api/scheduledtasks`

Class: `[Authorize]` (any authenticated user). Visibility is enforced in the handlers: Holders of `scheduledTasks.viewAll` see every task; everyone else sees only tasks they were named a viewer on.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /` | Any authenticated | Tasks visible to the caller. |
| `GET /{id}` | Any authenticated | One task with its items and viewers. |
| `GET /{id}/runs` | Any authenticated | Run history. Query: `take` (50). |
| `GET /{id}/runs/{runId}/file` | Any authenticated | Downloads one output file. Query: `fileName` (required). **Needs `scheduledTasks.download` or a viewer grant that allows downloads** — an Auditor can read the history but not the files. |
| `POST /` | `scheduledTasks.manage` | Creates a task. |
| `PUT /{id}` | `scheduledTasks.manage` | Replaces the task and recomputes its next run. |
| `DELETE /{id}` | `scheduledTasks.manage` | Deletes the task and its run history. |
| `POST /{id}/run` | `scheduledTasks.manage` | Queues an immediate run (202). |
| `POST /{id}/runs/{runId}/cancel` | `scheduledTasks.manage` | Cancels an in-flight run, aborting the database command. 202, or 409 when the run is not in progress. |

## Audit trail — `/api/admin/systemauditlogs`

Class: `[Authorize]`. Read-only by design — nothing writes here through the API.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /` | `audit.view` | The trail, newest first. Query: `category`, `action`, `userId`, `isSuccess`, `fromUtc`, `toUtc`, `search`, `pageNumber` (1), `pageSize` (25, max 200). |
| `GET /actions` | `audit.view` | `{ categories, actions }` — every code the server can emit, so the filter dropdowns cannot drift from reality. |

## System settings — `/api/admin/systemsettings`

Class: `[Authorize]`.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /` | Any authenticated | All settings. Readable by anyone signed in: the client reads `directoryEnabled` to decide whether to offer a menu entry. |
| `PUT /` | `settings.manage` | Replaces **all** settings — send the whole object. Out-of-range numbers are refused with 400 and a message naming the bounds. Audited as `settings.updated` with the full set. |

See [Runtime Settings in the README](../README.md#runtime-settings) for what each one does.

## Running queries — `/api/user/queries`

Class: `[Authorize]` (any authenticated user). No action overrides it; ownership of a job is
checked in code — the submitting user or an Admin, otherwise 403.

| Verb + path | Roles | Notes |
|---|---|---|
| `GET /` | `queries.run` | Queries the caller may run. |
| `GET /groups` | `queries.run` | The same set bucketed by query group, plus an "Ungrouped" bucket. |
| `GET /{id}` | `queries.run` | One accessible query; 403 when it is not. |
| `POST /{id}/execute` | `queries.run` | Runs it. Body `{ parameters, confirmed }`. A read caches its full result server-side and returns metadata plus a `jobId` — **not** the rows. A write returns its preview inline, or the affected count once `confirmed`. |
| `POST /{id}/execute-async` | `queries.run` | Queues a background run for a long-running query. 202 `{ jobId }`. |
| `GET /jobs/{jobId}` | Owner or Admin | Status, and result metadata once finished. **410** once the job is gone (see below). |
| `GET /jobs/{jobId}/rows` | Owner or Admin | One page of a cached result. Query: `pageIndex` (0), `pageSize` (25), `sortColumn`, `sortDir`, `filters` (JSON object of column → substring). 409 while the result is not ready; **410** once it is gone. |
| `GET /jobs/{jobId}/export-file` | Owner or Admin, **plus both export gates** | Downloads the cached result — no re-run. Query: `format` = `xlsx`/`excel` (default), `csv`, `json`, `pdf`, `docx`/`word`; anything else is 400. Word and PDF use the query's template, else the system default, else the built-in starter. **403** unless the query permits that format **and** the caller's role holds the matching `queries.export*` capability. **410** once the job is gone. |
| `POST /jobs/{jobId}/cancel` | Owner or Admin | Cancels the run and stops the database command. |
| `DELETE /jobs/{jobId}` | Owner or Admin | Releases the cached result immediately. Idempotent. |
| `GET /{queryId}/parameters/{parameterId}/dropdown-options` | `queries.run` | Options for a dropdown parameter — a static list or a lookup query. Capped by `query.maxRows`. |
| `GET /history` | `queries.run` | The caller's own execution history. Query: `sortBy`, `sortDescending` (true), `pageNumber` (1), `pageSize` (25). |
| `GET /history/{id}/old-values` | `queries.run` | Before-change snapshots for one of the caller's **own** logs. Query: `pageNumber` (1), `pageSize` (100). |

**410 Gone, and why it is not 404.** The job store is in-memory, so a job id stops resolving
once the result expires on its retention window, is released, or the worker process restarts
under it. All three are "it was here and now it isn't", not "no such thing" — the body carries
`{ message }` saying the result is no longer available and to run the query again, which the
client shows verbatim. Treat it as a terminal state for a poll loop: retrying the same id will
never succeed.

The whole controller needs `queries.run`, and the list returns only what the caller has been
granted. No seeded role but User and Admin holds it, which is why the client hides My Queries and
History from the others — but it is a permission like any other, and can be granted.
