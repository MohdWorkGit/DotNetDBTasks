/**
 * Captures every screenshot used by the user manual, once per language.
 *
 * Run it against a *running* dev stack (API + `ng serve`) — see README.md.
 *
 *   MANUAL_LOCALES=en,ar   which languages to capture (default: both)
 *   MANUAL_THEME=dark      which theme the screenshots use (default: dark)
 *
 * Output lands in screenshots/<locale>/, so the English manual gets English screenshots and
 * the Arabic manual gets right-to-left Arabic ones.
 *
 * Two things are deliberate rather than incidental:
 *
 *  - Sample records (a query with parameters, a write query, a group, a scheduled task) are
 *    discovered from the API at run time rather than hard-coded, so the script keeps working
 *    after the demo data changes.
 *  - Nothing here modifies data, and that is enforced rather than assumed: a route handler
 *    aborts every non-GET request except signing in and executing a query (WRITE_ALLOWLIST).
 *    The write-query screenshot uses the server-side *preview*, which runs inside a
 *    transaction that is always rolled back, and the run is then cancelled — Confirm is never
 *    clicked. This matters: the Website logo dialog's Remove button has no confirmation step,
 *    and an earlier version of this script deleted a live logo with it.
 */
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const APP = process.env.MANUAL_APP_URL || 'http://localhost:4200';
const API = process.env.MANUAL_API_URL || 'http://localhost:60187/api';
const ADMIN_USER = process.env.MANUAL_ADMIN_USER || 'admin';
const ADMIN_PASS = process.env.MANUAL_ADMIN_PASS || 'Admin@123';
const AUDITOR_USER = process.env.MANUAL_AUDITOR_USER || 'auditor';
const AUDITOR_PASS = process.env.MANUAL_AUDITOR_PASS || 'Auditor@123';
const LOCALES = (process.env.MANUAL_LOCALES || 'en,ar').split(',').map(s => s.trim());
const THEME = process.env.MANUAL_THEME || 'dark';

const I18N_DIR = path.join(__dirname, '..', '..', 'client', 'src', 'assets', 'i18n');
const LOCALE_LABEL = { en: 'English', ar: 'العربية' };

const log = (...a) => console.log('[manual]', ...a);

// ---------------------------------------------------------------- i18n lookup

/**
 * The interface is translated, so a selector like `button:has-text("Backup")` finds nothing in
 * the Arabic run. Reading the app's own catalogues keeps the selectors correct in every
 * language without a second copy of the strings living here.
 */
function loadCatalog(locale) {
  const file = path.join(I18N_DIR, `${locale}.json`);
  if (!fs.existsSync(file)) throw new Error(`missing translation catalogue: ${file}`);
  const json = JSON.parse(fs.readFileSync(file, 'utf8'));
  return (key) => {
    const value = key.split('.').reduce((o, k) => (o == null ? o : o[k]), json);
    if (typeof value !== 'string') throw new Error(`translation key not found: ${key}`);
    return value;
  };
}

// ---------------------------------------------------------------- helpers

async function settle(page, ms = 800) {
  try { await page.waitForLoadState('networkidle', { timeout: 20000 }); } catch {}
  // Several pages fan out to LDAP on load, which outlives networkidle; wait the spinner out.
  try {
    await page.waitForFunction(
      () => document.querySelectorAll('mat-spinner, .mat-mdc-progress-spinner').length === 0,
      { timeout: 90000 });
  } catch { log('   (spinner still visible — capturing anyway)'); }
  await page.waitForTimeout(ms);
}

/** Masks cells under sensitive column headers before a grid is photographed.
 *
 *  The write-query preview runs `SELECT * FROM <table>`, so a query against a user table puts
 *  real password hashes on screen — which must not end up printed in a manual. Purely visual:
 *  it edits the rendered page, never the data. */
const SENSITIVE = /password|hash|secret|token|salt|credential|apikey|كلمة.?المرور/i;
async function redactSensitiveColumns(page) {
  const masked = await page.evaluate((pattern) => {
    const re = new RegExp(pattern, 'i');
    let count = 0;
    for (const table of document.querySelectorAll('table')) {
      const headers = [...table.querySelectorAll('thead th, tr:first-child th')];
      headers.forEach((th, i) => {
        if (!re.test(th.textContent || '')) return;
        for (const row of table.querySelectorAll('tbody tr')) {
          const cell = row.children[i];
          if (cell && cell.textContent.trim()) { cell.textContent = '••••••••••'; count++; }
        }
      });
    }
    return count;
  }, SENSITIVE.source);
  if (masked) log(`   (masked ${masked} cell(s) under sensitive column headers)`);
}

// ---------------------------------------------------------------- sample data

async function discover() {
  const res = await fetch(`${API}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: ADMIN_USER, password: ADMIN_PASS })
  });
  if (!res.ok) throw new Error(`API login failed (${res.status}) — is the API running on ${API}?`);
  const auth = await res.json();
  const h = { Authorization: 'Bearer ' + auth.accessToken };
  const get = async (p) => {
    const r = await fetch(API + p, { headers: h });
    if (!r.ok) return [];
    return (await r.json().catch(() => [])) ?? [];
  };
  const unwrap = (j) => j.items ?? j;

  const queries = unwrap(await get('/admin/dynamicqueries'));
  const full = [];
  for (const q of queries) {
    const r = await fetch(`${API}/admin/dynamicqueries/${q.id}`, { headers: h });
    full.push(r.ok ? await r.json() : q);
  }

  const isSelect = (q) => (q.queryType ?? 0) === 0;
  const isWrite = (q) => [1, 2, 3].includes(q.queryType);
  const paramCount = (q) => (q.parameters || []).length;

  const read = full.filter(q => isSelect(q) && paramCount(q) > 0)
                   .sort((a, b) => paramCount(b) - paramCount(a))[0]
            || full.filter(isSelect)[0];
  const multi = full.find(q => isSelect(q) && (q.parameters || []).some(p => p.allowMultiple));
  const write = full.filter(isWrite).sort((a, b) => paramCount(b) - paramCount(a))[0];
  const groups = unwrap(await get('/admin/querygroups'));
  const tasks = unwrap(await get('/scheduledtasks'));

  const sample = {
    read: read?.id, readParams: read?.parameters || [], readName: read?.name,
    multi: multi?.id, multiName: multi?.name,
    write: write?.id, writeParams: write?.parameters || [], writeName: write?.name,
    group: groups[0]?.id, groupName: groups[0]?.name,
    task: tasks[0]?.id, taskName: tasks[0]?.name
  };
  log('sample data:', JSON.stringify({
    read: sample.readName, multi: sample.multiName, write: sample.writeName,
    group: sample.groupName, task: sample.taskName
  }));
  return sample;
}

// ---------------------------------------------------------------- one locale

async function captureLocale(browser, locale, sample) {
  const T = loadCatalog(locale);
  const out = path.join(__dirname, 'screenshots', locale);
  fs.mkdirSync(out, { recursive: true });

  const captured = [];
  const failed = [];
  const dark = THEME === 'dark';

  const ctx = await browser.newContext({
    viewport: { width: 1500, height: 950 },
    deviceScaleFactor: 1.5,
    locale: locale === 'ar' ? 'ar' : 'en-US'
  });

  // Seed the two preferences before the app boots, so no screenshot has to be taken through a
  // menu click and a page reload. Both services read these keys on construction.
  //
  // Only when absent: switching language reloads the page, so an unconditional seed would run
  // again on that reload and put the language straight back — which is exactly how the
  // "other language" screenshot came out in the wrong language.
  const seed = `
    if (!localStorage.getItem('theme-preference'))
      localStorage.setItem('theme-preference', ${JSON.stringify(JSON.stringify(dark))});
    if (!localStorage.getItem('language-preference'))
      localStorage.setItem('language-preference', ${JSON.stringify(locale)});
  `;
  await ctx.addInitScript(seed);

  // ---- write guard: screenshots must never change anything -----------------
  const WRITE_ALLOWLIST = [
    /\/api\/auth\/(login|refresh)$/,          // signing in
    /\/api\/user\/queries\/[^/]+\/execute/,   // running a query, incl. the rolled-back preview
    /\/api\/user\/queries\/jobs\//            // polling / releasing a cached result
  ];
  await ctx.route('**/api/**', async (route) => {
    const request = route.request();
    if (request.method() === 'GET') return route.continue();
    if (WRITE_ALLOWLIST.some(re => re.test(request.url()))) return route.continue();
    log(`   BLOCKED ${request.method()} ${request.url()} — the capture run must not write.`);
    return route.abort();
  });

  // The AD Users page lists directory departments, and a dev machine usually has no
  // directory to answer. Substituting an empty list only when the call actually fails keeps
  // that page screenshotable without pretending the directory returned data it did not.
  // (The Manage Access pages no longer call this: their groups come from the application.)
  await ctx.route('**/admin/ldap/departments', async (route) => {
    try {
      const response = await route.fetch();
      if (response.ok()) return route.fulfill({ response });
      log('   (AD departments unavailable — substituting an empty list so the page renders)');
    } catch {
      log('   (AD departments unreachable — substituting an empty list so the page renders)');
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
  });

  const page = await ctx.newPage();

  // ---- locale-aware primitives --------------------------------------------
  const shot = async (name, { full = false } = {}) => {
    await page.screenshot({ path: path.join(out, name + '.png'), fullPage: full });
    captured.push(name);
    log('saved', `${locale}/${name}`, full ? '(full page)' : '');
  };

  const step = async (name, fn) => {
    try { await fn(); }
    catch (e) {
      failed.push(`${name}: ${e.message.split('\n')[0]}`);
      log('!! FAILED', `${locale}/${name}`, '-', e.message.split('\n')[0]);
    }
  };

  const go = async (url, waitFor) => {
    await page.goto(APP + url, { waitUntil: 'domcontentloaded' });
    if (waitFor) {
      try { await page.waitForSelector(waitFor, { timeout: 30000 }); }
      catch { log('   (selector', waitFor, 'never appeared on', url + ')'); }
    }
    await settle(page);
  };

  const login = async (username, password) => {
    await page.goto(APP + '/login', { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('input[formcontrolname=username]', { timeout: 90000 });
    await settle(page, 400);
    await page.fill('input[formcontrolname=username]', username);
    await page.fill('input[formcontrolname=password]', password);
    await page.click('button[type=submit]');
    await page.waitForSelector('mat-toolbar', { timeout: 60000 });
    await settle(page);
    log('signed in as', username);
  };

  const logout = async () => {
    // clear() takes the theme and language with it; put them straight back.
    await page.evaluate((s) => { localStorage.clear(); eval(s); }, seed);
    await page.goto(APP + '/login', { waitUntil: 'domcontentloaded' });
    await settle(page, 500);
  };

  const menuTrigger = (i) => page.locator('mat-toolbar button[aria-haspopup=menu]').nth(i);
  const openAccountMenu = async () => {
    await page.locator('mat-toolbar button[aria-haspopup=menu]').last().click();
    await settle(page, 600);
  };
  const closeOverlay = async () => {
    await page.keyboard.press('Escape');
    await page.waitForTimeout(400);
  };
  /** A button carrying a translated label. */
  const clickLabel = async (key, scope = '') => {
    const sel = `${scope} button:has-text(${JSON.stringify(T(key))})`.trim();
    await page.click(sel);
  };
  /** Material tab labels are hard-coded English in the templates, so they need no lookup. */
  const openTab = async (label) => {
    await page.locator('.mat-mdc-tab, [role=tab]').filter({ hasText: label }).first().click();
    await settle(page, 600);
  };

  const fillParameters = async (params) => {
    for (const p of params) {
      const label = p.displayName || p.name;
      try {
        if (p.parameterType === 2) {
          await page.getByLabel(label, { exact: false }).first().fill('1/1/2000');
        } else if (p.parameterType === 1) {
          await page.getByLabel(label, { exact: false }).first().fill('1');
        } else if (p.parameterType === 4) {
          await page.locator('mat-select').first().click();
          await page.waitForTimeout(1200);
          const opt = page.locator('mat-option').first();
          if (await opt.count()) await opt.click();
          await page.waitForTimeout(400);
        } else if (p.parameterType === 0) {
          await page.getByLabel(label, { exact: false }).first().fill('sample');
        }
      } catch { /* optional parameter, or a control this heuristic cannot reach */ }
    }
    const dates = params.filter(p => p.parameterType === 2);
    if (dates.length > 1) {
      const last = dates[dates.length - 1];
      try {
        await page.getByLabel(last.displayName || last.name, { exact: false })
          .first().fill('12/31/2035');
      } catch {}
    }
  };

  const S = sample;

  // ---- 01 Login -----------------------------------------------------------
  await step('login page', async () => {
    await page.goto(APP + '/login', { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('input[formcontrolname=username]', { timeout: 90000 });
    await settle(page);
    await shot('01-login');
  });

  await login(ADMIN_USER, ADMIN_PASS);

  // ---- 02-06 Navigation ---------------------------------------------------
  await step('admin landing', async () => await shot('02-admin-landing'));
  await step('account menu', async () => {
    await openAccountMenu();
    await shot('03-account-menu');
    await closeOverlay();
  });
  for (const [i, name] of [[0, '04-nav-queries'], [1, '05-nav-people'], [2, '06-nav-audit']]) {
    await step(name, async () => {
      await menuTrigger(i).click();
      await settle(page, 600);
      await shot(name);
      await closeOverlay();
    });
  }

  // ---- 10-17 Queries ------------------------------------------------------
  await step('manage queries', async () => {
    await go('/admin/queries', 'table');
    await shot('10-manage-queries');
  });
  await step('backup menu', async () => {
    await clickLabel('admin.queries.backup');
    await settle(page, 600);
    await shot('11-backup-menu');
    await closeOverlay();
  });
  await step('default template menu', async () => {
    await clickLabel('admin.queries.defaultWordTemplate');
    await settle(page, 600);
    await shot('12-default-template-menu');
    await closeOverlay();
  });
  await step('create query form', async () => {
    await go('/admin/queries/create', 'form');
    await shot('13-query-create', { full: true });
  });
  await step('edit query form', async () => {
    await go('/admin/queries/edit/' + (S.write || S.read), 'form');
    await shot('14-query-edit-parameters', { full: true });
  });
  await step('query access', async () => {
    await go(`/admin/queries/${S.read}/roles`, 'mat-tab-group');
    await shot('15-query-access-roles');
    await openTab(T('admin.access.userGroupsTab'));
    await shot('16-query-access-user-groups');
    await openTab('Users');
    await shot('17-query-access-users');
  });

  // ---- 20-22 Query groups -------------------------------------------------
  await step('query groups', async () => {
    await go('/admin/query-groups', 'table');
    await shot('20-query-groups');
  });
  await step('create group', async () => {
    await go('/admin/query-groups/create', 'form');
    await shot('21-query-group-create');
  });
  await step('group access', async () => {
    await go(`/admin/query-groups/${S.group}/access`, 'mat-tab-group');
    await shot('22-query-group-access');
  });

  // ---- 30-33 Scheduled tasks ----------------------------------------------
  await step('scheduled tasks list', async () => {
    await go('/admin/scheduled-tasks', 'table, mat-card');
    await shot('30-scheduled-tasks');
  });
  await step('create scheduled task', async () => {
    await go('/admin/scheduled-tasks/create', 'form');
    await shot('31-scheduled-task-create', { full: true });
  });
  await step('edit scheduled task', async () => {
    await go(`/admin/scheduled-tasks/edit/${S.task}`, 'form');
    await shot('32-scheduled-task-edit', { full: true });
  });
  await step('scheduled task runs', async () => {
    await go(`/admin/scheduled-tasks/${S.task}/runs`, '.container');
    const panel = page.locator('mat-expansion-panel-header').first();
    if (await panel.count()) { await panel.click(); await settle(page, 900); }
    await shot('33-scheduled-task-runs', { full: true });
  });

  // ---- 40-41 Audit trails -------------------------------------------------
  await step('execution logs', async () => {
    await go('/admin/logs', 'table');
    await shot('40-execution-logs');
  });
  await step('system audit', async () => {
    await go('/admin/system-audit', 'table');
    await shot('41-system-audit');
  });

  // ---- 50-58 Administration -----------------------------------------------
  await step('system settings', async () => {
    await go('/admin/settings', 'mat-slide-toggle');
    await shot('50-system-settings');
  });
  await step('user management', async () => {
    await go('/admin/users', 'table');
    await shot('51-user-management');
  });
  await step('create user form', async () => {
    await clickLabel('admin.users.createUser');
    await settle(page, 700);
    await shot('52-user-create-form', { full: true });
  });
  await step('permissions tab', async () => {
    await go('/admin/settings', 'mat-tab-group');
    await openTab(T('admin.permissions.tab'));
    await shot('51-permissions', { full: true });
  });
  await step('user groups', async () => {
    await go('/admin/user-groups', '.container');
    await shot('52a-user-groups');
  });
  await step('user group form', async () => {
    await go('/admin/user-groups/create', 'form');
    await shot('52b-user-group-create', { full: true });
  });
  await step('database users', async () => {
    await go('/admin/database-users', '.container');
    await shot('53-database-users');
  });
  await step('database user form', async () => {
    await clickLabel('admin.dbUsers.add');
    await settle(page, 700);
    await shot('54-database-user-form', { full: true });
  });
  await step('AD users', async () => {
    await go('/admin/ad-users', 'mat-tab-group');
    await shot('55-ad-users-search');
    await openTab('Departments');
    await shot('56-ad-users-departments');
    await openTab(T('admin.adUsers.importedTab'));
    await shot('57-ad-users-imported');
  });
  await step('branding dialog', async () => {
    await go('/admin/queries', 'table');
    await openAccountMenu();
    await clickLabel('nav.websiteBranding');
    await settle(page, 900);
    await shot('58-branding-dialog');
    // The dialog holds three sections and its content scrolls, so the site-name fields sit
    // below the fold — a second frame rather than a caption that describes what is cropped.
    await page.evaluate(() => {
      const content = document.querySelector('app-branding-dialog mat-dialog-content');
      if (content) content.scrollTop = content.scrollHeight;
    });
    await settle(page, 600);
    await shot('58b-branding-dialog-name');
    await closeOverlay();
  });

  // ---- 60-70 Running queries ----------------------------------------------
  await step('my queries', async () => {
    await go('/user/queries', '.container');
    await shot('60-my-queries', { full: true });
  });
  await step('execute parameters', async () => {
    await go(`/user/queries/${S.read}/execute`, 'form');
    await shot('61-execute-parameters');
  });
  await step('execute results', async () => {
    await fillParameters(S.readParams);
    await settle(page, 400);
    await page.click('button[type=submit]');
    await page.waitForSelector('.results-card', { timeout: 120000 });
    await settle(page, 1200);
    await redactSensitiveColumns(page);
    await shot('62-execute-results', { full: true });
    await clickLabel('user.execute.export');
    await settle(page, 600);
    await shot('63-export-menu');
    await closeOverlay();
  });
  await step('multi-value parameter', async () => {
    if (!S.multi) throw new Error('no query with a multi-value parameter in this database');
    await go(`/user/queries/${S.multi}/execute`, 'form');
    await shot('64-execute-multivalue');
  });
  await step('write query preview', async () => {
    if (!S.write) throw new Error('no write query in this database');
    await go(`/user/queries/${S.write}/execute`, 'form');
    await shot('65-execute-write-form');
    await fillParameters(S.writeParams);
    await settle(page, 500);
    await shot('66-execute-write-filled');
    await page.click('button[type=submit]');
    await page.waitForSelector('.confirm-card', { timeout: 120000 });
    await settle(page, 900);
    await redactSensitiveColumns(page);
    await shot('67-execute-write-preview', { full: true });
    // Preview only — the server already rolled its transaction back. Never click Confirm.
    await clickLabel('common.cancel', '.confirm-card');
    await settle(page, 600);
  });
  await step('execution history', async () => {
    await go('/user/history', 'table');
    await shot('70-execution-history');
  });

  // ---- 80-81 The alternatives to the defaults ------------------------------
  // The manual is illustrated in the default theme and its own language, so these two show
  // what the *other* choice looks like.
  await step('alternate theme', async () => {
    await go('/admin/queries', 'table');
    await openAccountMenu();
    await clickLabel(dark ? 'nav.switchToLight' : 'nav.switchToDark');
    await settle(page, 800);
    await closeOverlay();
    await shot('80-alternate-theme');
    await openAccountMenu();
    await clickLabel(dark ? 'nav.switchToDark' : 'nav.switchToLight');
    await settle(page, 700);
    await closeOverlay();
  });
  await step('alternate language', async () => {
    const other = locale === 'ar' ? 'en' : 'ar';
    await openAccountMenu();
    await page.click(`button:has-text(${JSON.stringify(LOCALE_LABEL[other])})`);
    await page.waitForSelector('mat-toolbar', { timeout: 60000 });
    await settle(page, 1500);
    await shot('81-alternate-language');
    await openAccountMenu();
    await page.click(`button:has-text(${JSON.stringify(LOCALE_LABEL[locale])})`);
    await page.waitForSelector('mat-toolbar', { timeout: 60000 });
    await settle(page, 1500);
  });

  // ---- 90-93 Auditor's view -----------------------------------------------
  await step('auditor', async () => {
    await logout();
    await login(AUDITOR_USER, AUDITOR_PASS);
    await shot('90-auditor-landing');
    await openAccountMenu();
    await shot('91-auditor-account-menu');
    await closeOverlay();
    await go('/admin/system-audit', 'table');
    await shot('92-auditor-system-audit');
  });

  await ctx.close();
  return { locale, captured, failed, out };
}

// ---------------------------------------------------------------- entry point

(async () => {
  const sample = await discover();
  const browser = await chromium.launch();
  const results = [];
  for (const locale of LOCALES) {
    log(`\n=== capturing ${locale} (${THEME} theme) ===`);
    results.push(await captureLocale(browser, locale, sample));
  }
  await browser.close();

  log('');
  for (const r of results) {
    log(`${r.locale}: ${r.captured.length} screenshots -> ${r.out}`);
    r.failed.forEach(f => log('   !!', f));
  }
  if (results.some(r => r.failed.length)) {
    log('The manual still builds; any missing image is replaced by a placeholder note.');
  }
})().catch(e => { console.error('FATAL', e); process.exit(1); });
