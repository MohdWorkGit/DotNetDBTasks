# User manual (generated)

`Bayan-User-Manual-EN.docx` and `-AR.docx` are **generated**, not hand-edited. After a
change to the application, regenerate them so the screenshots and the text match what shipped.

## Regenerate

```bash
# 1. Start the app (two terminals, from the repo root)
dotnet run --no-launch-profile --urls http://localhost:60187   # in src/Bayan.API
npx ng serve --port 4200                                        # in client/

# 2. Build the manual (from this folder)
npm install            # first time only
python -m pip install python-docx   # first time only
npm run manual         # capture screenshots + build both .docx files
```

`npm run manual` runs the two halves in order. They can also be run separately:

| Command | Does |
|---|---|
| `npm run capture` | Drives the running app with Playwright and writes `screenshots/en/` and `screenshots/ar/` |
| `npm run build` | Reads those screenshots and writes both `.docx` files |
| `python build-docx.py en` | Builds one language only |

Finally, open each document in Word, right-click the **Contents** table and choose **Update
Field** — Word fills in the page numbers on open, not at build time.

## What lives where

| File | Contains |
|---|---|
| `build-docx.py` | **The manual's text**, both languages, plus the Word layout |
| `capture-screenshots.js` | Which screens are photographed and how they are reached |
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
| `MANUAL_LOCALES` | `en,ar` | Languages to capture |
| `MANUAL_THEME` | `dark` | `dark` or `light` — the theme the screenshots use |
| `MANUAL_ADMIN_USER` / `MANUAL_ADMIN_PASS` | `admin` / `Admin@123` | Account used for most screens |
| `MANUAL_AUDITOR_USER` / `MANUAL_AUDITOR_PASS` | `auditor` / `Auditor@123` | Used for the "what an Auditor sees" figures |

## The capture run cannot write

Screenshots must never change data, and that is enforced rather than trusted: a Playwright
route handler **aborts every non-GET request** except signing in and executing a query. This is
not paranoia — the Website logo dialog's **Remove** button commits immediately with no
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
  first scheduled task. Changing the demo data does not break the capture.
- **Screenshots track the app's own translations.** Selectors resolve their labels from
  `client/src/assets/i18n/{en,ar}.json`, so a renamed button does not silently break the Arabic
  run.
- **Active Directory need not be reachable.** When `/admin/ldap/departments` fails, the capture
  substitutes an empty list so the AD Users page still renders. That page is the only one that
  reads the directory: access is granted through the application's own user groups, so the two
  Manage Access pages no longer call AD at all.
