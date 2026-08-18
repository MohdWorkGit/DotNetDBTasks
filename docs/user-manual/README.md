# User manual (generated)

`Bayan-User-Manual-EN.docx` and `-AR.docx` are **generated**, not hand-edited. After a
change to the application, regenerate them so the screenshots and the text match what shipped.

## Regenerate

```bash
# 1. The business tables the manual's example queries read (first time, or to reset them)
#    from the repo root, with NLS_LANG=.AL32UTF8 so the Arabic rows load intact
sqlplus test/test@host/service @database/demo-data.sql

# 2. Start the app (two terminals, from the repo root)
dotnet run --no-launch-profile --urls http://localhost:60187   # in src/Bayan.API
npx ng serve --port 4200                                        # in client/

# 3. Build the manual (from this folder)
npm install            # first time only
python -m pip install python-docx   # first time only
npm run manual         # seed + capture (per language) + build both .docx files
```

`npm run manual` runs four steps in order — seed English, capture English, seed Arabic,
capture Arabic — and then builds. They can also be run separately:

| Command | Does |
|---|---|
| `npm run seed` | Rebuilds the demo queries, groups, user groups, scheduled task and history (English; add `-- --lang ar` for Arabic) |
| `npm run capture` | Drives the running app with Playwright and writes `screenshots/en/` and `screenshots/ar/` (add `-- --locales ar` for one language) |
| `npm run build` | Reads those screenshots and writes both `.docx` files |
| `python build-docx.py en` | Builds one language only |

**Why the demo data is seeded twice.** A query, a query group and a user group each carry one
name and one description — the application does not translate content an administrator typed.
So the only way the Arabic manual can show Arabic query names is to seed the demo data in
Arabic before the Arabic screenshots are taken, and back in English before the English ones.
`seed-demo-data.js` holds both languages, one `T("English", "العربية")` pair per string, and
the Arabic pass runs last, so the database is left holding the Arabic demo content.

Finally, open each document in Word, right-click the **Contents** table and choose **Update
Field** — Word fills in the page numbers on open, not at build time.

## What lives where

| File | Contains |
|---|---|
| `build-docx.py` | **The manual's text**, both languages, plus the Word layout |
| `capture-screenshots.js` | Which screens are photographed and how they are reached |
| `seed-demo-data.js` | **The demo content**: the queries, groups, user groups, accounts, scheduled task and run history the screenshots show, in both languages |
| `../../database/demo-data.sql` | The business tables those queries read — customers, orders, products, employees, invoices |
| `screenshots/<lang>/` | Generated PNGs — safe to delete, recreated by `npm run capture` |

To add a section, edit `build-docx.py`. Every string is written once as
`T("English", "العربية")`, so the two documents cannot drift apart — adding a paragraph forces
you to supply both languages. To add a screenshot, add a `step(...)` block in
`capture-screenshots.js` and a `figure(...)` call in `build-docx.py`.

## Configuration

Environment variables, all optional:

| Variable | Default | Purpose |
|---|---|---|
| `MANUAL_APP_URL` | `http://localhost:4200` | Angular client |
| `MANUAL_API_URL` | `http://localhost:60187/api` | .NET API |
| `MANUAL_LOCALES` | `en,ar` | Languages to capture (or `--locales ar` on the command line) |
| `MANUAL_SEED_LANG` | `en` | Language of the seeded demo content (or `--lang ar`) |
| `MANUAL_DEMO_PASS` | `Demo@123` | Password given to the seeded demo accounts |
| `MANUAL_TASK_FOLDER` | `C:\Bayan\exports\sales` | Output folder of the seeded scheduled task |
| `MANUAL_THEME` | `dark` | `dark` or `light` — the theme the screenshots use |
| `MANUAL_ADMIN_USER` / `MANUAL_ADMIN_PASS` | `admin` / `Admin@123` | Account used for most screens |
| `MANUAL_AUDITOR_USER` / `MANUAL_AUDITOR_PASS` | `auditor` / `Auditor@123` | Used for the "what an Auditor sees" figures |

## The capture run cannot write

Screenshots must never change data, and that is enforced rather than trusted: a Playwright
route handler **aborts every non-GET request** except signing in and executing a query. This is
not paranoia — the Website branding dialog's **Remove** button commits immediately with no
confirmation step, and an earlier version of this script deleted a live site logo with it.

The one screenshot of a data-changing query uses the server-side *preview*, which runs inside a
transaction that is always rolled back; the run is then cancelled, and **Confirm is never
clicked**.

Cells under column headers matching `password`, `hash`, `secret`, `token`, `salt`, `credential`
or `apikey` are masked in the rendered page before the grid is photographed — a write-query
preview runs `SELECT *`, which otherwise prints real password hashes into the manual.

## Notes

- **Sample records are discovered at run time**, not hard-coded: the script picks a read query
  with the most parameters, a multi-value query, a write query, the first query group and the
  first scheduled task. Changing the demo data does not break the capture. The seeded set is
  built around that: "Orders by Period and Status" is the query with the most parameters and
  every one of them is fillable by the capture's heuristic, "Sales by Region" is the only
  multi-value one, and "Adjust Product Stock Level" is the write query with the most.
- **Seeding is destructive on the Bayan side.** `seed-demo-data.js` deletes every scheduled
  task, query and group before creating its own, and deleting a query cascades to its execution
  logs. It never deletes user accounts — the demo accounts it needs are created if missing and
  reused otherwise.
- **Screenshots track the app's own translations.** Selectors resolve their labels from
  `client/src/assets/i18n/{en,ar}.json`, so a renamed button does not silently break the Arabic
  run.
- **Active Directory need not be reachable.** When `/admin/ldap/departments` fails, the capture
  substitutes an empty list so the AD Users page still renders. That page is the only one that
  reads the directory: access is granted through the application's own user groups, so the two
  Manage Access pages no longer call AD at all.
