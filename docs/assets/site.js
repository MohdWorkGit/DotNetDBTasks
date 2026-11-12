/*
 * Jadwal documentation — shared behaviour for every page.
 *
 * Loaded in <head> without defer: the first part sets the language, direction and theme before
 * anything paints, so an Arabic reader never sees the English page flash first. The rest runs
 * once the page is parsed: it adds the top bar, the page list, the on-page contents and the
 * previous/next links, and draws every <figure class="chart"> from the JSON inside it.
 *
 * Works from file:// with no server. Saved choices live in localStorage, which a locked-down
 * browser may refuse; every access is guarded, and the page works the same without it.
 */
(function () {
  'use strict';

  var LANG_KEY = 'jadwal-docs-lang';
  var THEME_KEY = 'jadwal-docs-theme';
  var root = document.documentElement;

  function load(key) { try { return window.localStorage.getItem(key); } catch (e) { return null; } }
  function save(key, value) { try { window.localStorage.setItem(key, value); } catch (e) { /* not available */ } }

  function langFromUrl() {
    var m = /[?&]lang=(en|ar)\b/.exec(window.location.search);
    return m ? m[1] : null;
  }

  var lang = langFromUrl() || load(LANG_KEY) || 'en';
  if (lang !== 'ar') lang = 'en';
  var theme = load(THEME_KEY);

  function applyLang() {
    root.setAttribute('lang', lang);
    root.setAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
  }
  function applyTheme() {
    if (theme === 'light' || theme === 'dark') root.setAttribute('data-theme', theme);
    else root.removeAttribute('data-theme');
  }
  applyLang();
  applyTheme();

  /* ------------------------------------------------------------------ pages */

  var ICONS = {
    home: '<path d="M3 11l9-7 9 7v9a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z"/>',
    grid: '<rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/>',
    shield: '<path d="M12 3l8 3v6c0 4.5-3.4 8.3-8 9-4.6-.7-8-4.5-8-9V6z"/><path d="M9 12l2 2 4-4"/>',
    spark: '<path d="M12 3v4M12 17v4M3 12h4M17 12h4M6 6l2.5 2.5M15.5 15.5L18 18M6 18l2.5-2.5M15.5 8.5L18 6"/>',
    box: '<path d="M3 7l9-4 9 4v10l-9 4-9-4z"/><path d="M3 7l9 4 9-4M12 11v10"/>',
    server: '<rect x="3" y="4" width="18" height="7" rx="1.5"/><rect x="3" y="13" width="18" height="7" rx="1.5"/><path d="M7 7.5h.01M7 16.5h.01"/>',
    plug: '<path d="M9 3v5M15 3v5M6 8h12v3a6 6 0 0 1-12 0zM12 17v4"/>',
    db: '<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5"/><path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>',
    code: '<path d="M8 6l-6 6 6 6M16 6l6 6-6 6"/>',
    wrench: '<path d="M14.7 6.3a4 4 0 0 0-5.4 5.4L3 18l3 3 6.3-6.3a4 4 0 0 0 5.4-5.4l-2.6 2.6-2.4-.6-.6-2.4z"/>'
  };

  var PAGES = [
    { id: 'index', icon: 'home', group: 0, en: 'Home', ar: 'الرئيسية' },
    { id: 'features', icon: 'grid', group: 0, en: 'Features', ar: 'الخصائص' },
    { id: 'security', icon: 'shield', group: 0, en: 'Security and permissions', ar: 'الأمان والصلاحيات' },
    { id: 'ai', icon: 'spark', group: 0, en: 'AI assistant', ar: 'المساعد الذكي' },
    { id: 'deploy', icon: 'box', group: 1, en: 'Deployment', ar: 'النشر والتثبيت' },
    { id: 'iis', icon: 'server', group: 1, en: 'IIS hosting and sign-in', ar: 'الاستضافة على IIS' },
    { id: 'troubleshooting', icon: 'wrench', group: 1, en: 'Troubleshooting', ar: 'حل المشكلات' },
    { id: 'api', icon: 'plug', group: 2, en: 'API reference', ar: 'مرجع API' },
    { id: 'database', icon: 'db', group: 2, en: 'Database', ar: 'قاعدة البيانات' },
    { id: 'development', icon: 'code', group: 2, en: 'Development', ar: 'التطوير' }
  ];
  var GROUPS = [
    { en: 'Overview', ar: 'نظرة عامة' },
    { en: 'Install and run', ar: 'التثبيت والتشغيل' },
    { en: 'For developers', ar: 'للمطورين' }
  ];

  function svgIcon(name) {
    return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + ICONS[name] + '</svg>';
  }
  function both(o) { return '<span lang="en">' + o.en + '</span><span lang="ar">' + o.ar + '</span>'; }
  function pick(o) { return o == null ? '' : (typeof o === 'string' || typeof o === 'number') ? String(o) : (o[lang] != null ? o[lang] : o.en); }
  function esc(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function currentPage() {
    var id = document.body.getAttribute('data-page') || 'index';
    for (var i = 0; i < PAGES.length; i++) if (PAGES[i].id === id) return i;
    return 0;
  }

  /* -------------------------------------------------------------- the shell */

  function buildShell() {
    var main = document.querySelector('main');
    if (!main) return;
    var idx = currentPage();

    var top = document.createElement('header');
    top.className = 'topbar';
    top.innerHTML =
      '<button class="tb-btn menu-btn" type="button" aria-label="Menu" aria-expanded="false">' +
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 6h16M4 12h16M4 18h16"/></svg></button>' +
      '<a class="brand" href="index.html"><span class="brand-mark">' +
        '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><rect x="3.5" y="4" width="17" height="16" rx="2.5"/><path d="M3.5 9.5h17M9.5 9.5V20"/></svg>' +
        '</span><span><span lang="en">Jadwal</span><span lang="ar">جدول</span></span>' +
        '<span class="brand-sub">' + both({ en: 'Documentation', ar: 'التوثيق' }) + '</span></a>' +
      '<span class="spacer"></span>' +
      '<button class="tb-btn lang-btn" type="button">' +
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"/></svg>' +
        '<span lang="en" class="lbl">العربية</span><span lang="ar" class="lbl">English</span></button>' +
      '<button class="tb-btn theme-btn" type="button">' +
        '<svg class="sun" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>' +
        '<svg class="moon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>' +
        '</button>';

    var nav = document.createElement('nav');
    nav.className = 'sidebar';
    nav.setAttribute('aria-label', 'Pages');
    var html = '';
    GROUPS.forEach(function (g, gi) {
      html += '<div class="nav-group"><div class="nav-title">' + both(g) + '</div>';
      PAGES.forEach(function (p, pi) {
        if (p.group !== gi) return;
        html += '<a class="nav-link" href="' + p.id + '.html"' + (pi === idx ? ' aria-current="page"' : '') + '>' +
          '<span class="ico">' + svgIcon(p.icon) + '</span>' + both(p) + '</a>';
      });
      html += '</div>';
    });
    nav.innerHTML = html;

    var toc = document.createElement('aside');
    toc.className = 'toc';
    toc.setAttribute('aria-label', 'On this page');

    var layout = document.createElement('div');
    layout.className = 'layout';
    main.parentNode.insertBefore(top, main);
    main.parentNode.insertBefore(layout, main);
    layout.appendChild(nav);
    layout.appendChild(main);
    layout.appendChild(toc);
    var scrim = document.createElement('div');
    scrim.className = 'scrim';
    document.body.appendChild(scrim);

    // Previous / next
    var content = main.querySelector('.content') || main;
    var pager = document.createElement('nav');
    pager.className = 'pager';
    var prev = PAGES[idx - 1], next = PAGES[idx + 1];
    pager.innerHTML =
      (prev ? '<a class="prev" href="' + prev.id + '.html"><small>' + both({ en: 'Previous', ar: 'السابق' }) + '</small>' + both(prev) + '</a>' : '<span></span>') +
      (next ? '<a class="next" href="' + next.id + '.html"><small>' + both({ en: 'Next', ar: 'التالي' }) + '</small>' + both(next) + '</a>' : '');
    content.appendChild(pager);
    var foot = document.createElement('footer');
    foot.className = 'site-foot';
    foot.innerHTML = both({
      en: 'Jadwal documentation. Opens straight from disk, with no internet needed.',
      ar: 'توثيق «جدول». يُفتح مباشرة من القرص دون حاجة إلى الإنترنت.'
    });
    content.appendChild(foot);

    top.querySelector('.lang-btn').addEventListener('click', function () {
      lang = lang === 'ar' ? 'en' : 'ar';
      save(LANG_KEY, lang);
      applyLang();
      updateUrlLang();
      afterLangChange();
    });
    top.querySelector('.theme-btn').addEventListener('click', function () {
      var dark = theme === 'dark' || (theme !== 'light' && window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
      theme = dark ? 'light' : 'dark';
      save(THEME_KEY, theme);
      applyTheme();
      updateThemeLabel();
    });
    var menu = top.querySelector('.menu-btn');
    function closeNav() { document.body.classList.remove('nav-open'); menu.setAttribute('aria-expanded', 'false'); }
    menu.addEventListener('click', function () {
      var open = document.body.classList.toggle('nav-open');
      menu.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
    scrim.addEventListener('click', closeNav);
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeNav(); });

    // Carry the language across page links: file:// pages may not share localStorage.
    document.addEventListener('click', function (e) {
      var a = e.target.closest ? e.target.closest('a[href]') : null;
      if (!a) return;
      var href = a.getAttribute('href');
      if (!/^[\w-]+\.html(#.*)?$/.test(href)) return;
      var parts = href.split('#');
      a.setAttribute('href', parts[0] + '?lang=' + lang + (parts[1] ? '#' + parts[1] : ''));
    }, true);
  }

  function updateUrlLang() {
    if (!window.history || !window.history.replaceState) return;
    try {
      var url = window.location.pathname + '?lang=' + lang + window.location.hash;
      window.history.replaceState(null, '', url);
    } catch (e) { /* file:// in some browsers */ }
  }

  function updateThemeLabel() {
    var btn = document.querySelector('.theme-btn');
    if (!btn) return;
    var dark = root.getAttribute('data-theme') === 'dark' ||
      (!root.getAttribute('data-theme') && window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
    var label = lang === 'ar'
      ? (dark ? 'التبديل إلى الوضع الفاتح' : 'التبديل إلى الوضع الداكن')
      : (dark ? 'Switch to light mode' : 'Switch to dark mode');
    btn.setAttribute('aria-label', label);
    btn.setAttribute('title', label);
    var lb = document.querySelector('.lang-btn');
    if (lb) lb.setAttribute('aria-label', lang === 'ar' ? 'Switch to English' : 'التبديل إلى العربية');
  }

  /* ------------------------------------------------------- on-page contents */

  function visible(el) { return !!(el && el.offsetParent !== null); }

  function buildToc() {
    var toc = document.querySelector('.toc');
    if (!toc) return;
    var sections = Array.prototype.slice.call(document.querySelectorAll('main section[id]'));
    var items = [];
    sections.forEach(function (s) {
      var heads = s.querySelectorAll('h2');
      for (var i = 0; i < heads.length; i++) {
        if (visible(heads[i])) { items.push({ id: s.id, text: heads[i].textContent.replace('#', '').trim(), el: s }); break; }
      }
    });
    if (items.length < 2) { toc.innerHTML = ''; return; }
    toc.innerHTML = '<div class="toc-title">' + (lang === 'ar' ? 'في هذه الصفحة' : 'On this page') + '</div>' +
      items.map(function (it) { return '<a href="#' + it.id + '">' + esc(it.text) + '</a>'; }).join('');
    var links = toc.querySelectorAll('a');
    function onScroll() {
      var y = window.scrollY + 120, active = 0;
      items.forEach(function (it, i) { if (it.el.offsetTop <= y) active = i; });
      for (var i = 0; i < links.length; i++) links[i].classList.toggle('active', i === active);
    }
    window.removeEventListener('scroll', window.__jadwalTocScroll || function () {});
    window.__jadwalTocScroll = onScroll;
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
  }

  function addAnchors() {
    var sections = document.querySelectorAll('main section[id]');
    for (var i = 0; i < sections.length; i++) {
      var heads = sections[i].querySelectorAll('h2');
      for (var j = 0; j < heads.length; j++) {
        if (heads[j].querySelector('.anchor-link')) continue;
        var a = document.createElement('a');
        a.className = 'anchor-link';
        a.href = '#' + sections[i].id;
        a.setAttribute('aria-hidden', 'true');
        a.textContent = '#';
        heads[j].appendChild(a);
      }
    }
  }

  function addCopyButtons() {
    var pres = document.querySelectorAll('main pre');
    for (var i = 0; i < pres.length; i++) {
      (function (pre) {
        if (pre.querySelector('.copy-btn')) return;
        var b = document.createElement('button');
        b.type = 'button';
        b.className = 'copy-btn';
        b.textContent = lang === 'ar' ? 'نسخ' : 'Copy';
        b.addEventListener('click', function () {
          var text = pre.innerText.replace(/(Copy|نسخ|Copied|تم النسخ)\s*$/, '').replace(/\n$/, '');
          var done = function () { b.textContent = lang === 'ar' ? 'تم النسخ' : 'Copied'; setTimeout(function () { b.textContent = lang === 'ar' ? 'نسخ' : 'Copy'; }, 1400); };
          if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done, function () { fallbackCopy(text); done(); });
          } else { fallbackCopy(text); done(); }
        });
        pre.appendChild(b);
      })(pres[i]);
    }
  }
  function fallbackCopy(text) {
    var t = document.createElement('textarea');
    t.value = text;
    t.style.position = 'fixed';
    t.style.opacity = '0';
    document.body.appendChild(t);
    t.select();
    try { document.execCommand('copy'); } catch (e) { /* nothing more to try */ }
    document.body.removeChild(t);
  }

  /* ----------------------------------------------------------- page filters */

  function setupFilters() {
    var inputs = document.querySelectorAll('input[data-filter]');
    for (var i = 0; i < inputs.length; i++) {
      (function (input) {
        var scope = document.querySelector(input.getAttribute('data-filter'));
        if (!scope) return;
        var count = input.parentNode.querySelector('.search-count');
        var empty = scope.parentNode.querySelector('.empty-note[lang="' + input.closest('[lang]').getAttribute('lang') + '"]') || null;
        function run() {
          var q = input.value.trim().toLowerCase();
          var rows = scope.querySelectorAll('[data-item]');
          var shown = 0;
          for (var r = 0; r < rows.length; r++) {
            var hit = !q || rows[r].textContent.toLowerCase().indexOf(q) !== -1;
            rows[r].style.display = hit ? '' : 'none';
            if (hit) shown++;
          }
          var groups = scope.querySelectorAll('[data-group]');
          for (var g = 0; g < groups.length; g++) {
            var any = groups[g].querySelectorAll('[data-item]:not([style*="none"])').length > 0;
            groups[g].style.display = any ? '' : 'none';
          }
          if (count) count.textContent = q ? String(shown) : '';
          if (empty) empty.style.display = shown ? 'none' : 'block';
        }
        input.addEventListener('input', run);
      })(inputs[i]);
    }
  }

  /* ----------------------------------------------------------------- charts */
  /*
   * Rules followed (dataviz): one series in one colour, a legend only for two or more, text in
   * ink tokens never in the series colour, bars at most 24px thick with a rounded data end and a
   * square end on the baseline, 2px surface gaps, recessive grid, a tooltip on every mark and a
   * table view beside every chart. Colours come from CSS tokens, so dark mode needs no redraw.
   */

  var tip;
  function tooltip() {
    if (!tip) {
      tip = document.createElement('div');
      tip.className = 'chart-tip';
      tip.setAttribute('role', 'status');
      document.body.appendChild(tip);
    }
    return tip;
  }
  function showTip(title, value, x, y) {
    var t = tooltip();
    t.textContent = '';
    var b = document.createElement('b');
    b.textContent = title;
    var v = document.createElement('div');
    v.className = 'tv';
    v.textContent = value;
    t.appendChild(b);
    t.appendChild(v);
    t.classList.add('show');
    var w = t.offsetWidth, h = t.offsetHeight;
    var left = Math.min(Math.max(8, x + 14), window.innerWidth - w - 8);
    var top = y - h - 12 < 8 ? y + 16 : y - h - 12;
    t.style.left = left + 'px';
    t.style.top = top + 'px';
  }
  function hideTip() { if (tip) tip.classList.remove('show'); }

  var NS = 'http://www.w3.org/2000/svg';
  function el(name, attrs, parent) {
    var n = document.createElementNS(NS, name);
    for (var k in attrs) if (attrs.hasOwnProperty(k)) n.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(n);
    return n;
  }
  function txt(parent, x, y, s, attrs) {
    var a = attrs || {};
    a.x = x; a.y = y;
    var t = el('text', a, parent);
    t.textContent = s;
    return t;
  }
  function fmt(n, digits) {
    return Number(n).toLocaleString('en-US', { maximumFractionDigits: digits == null ? 1 : digits });
  }
  function niceMax(v) {
    if (v <= 0) return 1;
    var p = Math.pow(10, Math.floor(Math.log10(v)));
    var steps = [1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10];
    for (var i = 0; i < steps.length; i++) if (steps[i] * p >= v) return steps[i] * p;
    return 10 * p;
  }
  function ticks(max, n) {
    var step = niceMax(max / n);
    var out = [];
    for (var v = 0; v <= max + 1e-9; v += step) out.push(v);
    return out;
  }
  /* A bar with its data end rounded (4px) and its baseline end square. dir: 'right' | 'left' | 'up'. */
  function barPath(x, y, w, h, dir) {
    var r = Math.min(4, (dir === 'up' ? w : h) / 2, (dir === 'up' ? h : w));
    if (w <= 0 || h <= 0) return '';
    if (dir === 'up') {
      return 'M' + x + ',' + (y + h) + 'V' + (y + r) + 'Q' + x + ',' + y + ' ' + (x + r) + ',' + y +
        'H' + (x + w - r) + 'Q' + (x + w) + ',' + y + ' ' + (x + w) + ',' + (y + r) + 'V' + (y + h) + 'Z';
    }
    if (dir === 'left') {
      return 'M' + (x + w) + ',' + y + 'H' + (x + r) + 'Q' + x + ',' + y + ' ' + x + ',' + (y + r) +
        'V' + (y + h - r) + 'Q' + x + ',' + (y + h) + ' ' + (x + r) + ',' + (y + h) + 'H' + (x + w) + 'Z';
    }
    return 'M' + x + ',' + y + 'H' + (x + w - r) + 'Q' + (x + w) + ',' + y + ' ' + (x + w) + ',' + (y + r) +
      'V' + (y + h - r) + 'Q' + (x + w) + ',' + (y + h) + ' ' + (x + w - r) + ',' + (y + h) + 'H' + x + 'Z';
  }
  /* A floating bar, both ends rounded. */
  function pillPath(x, y, w, h) {
    var r = Math.min(4, h / 2, w / 2);
    return 'M' + (x + r) + ',' + y + 'H' + (x + w - r) + 'Q' + (x + w) + ',' + y + ' ' + (x + w) + ',' + (y + r) +
      'V' + (y + h - r) + 'Q' + (x + w) + ',' + (y + h) + ' ' + (x + w - r) + ',' + (y + h) +
      'H' + (x + r) + 'Q' + x + ',' + (y + h) + ' ' + x + ',' + (y + h - r) + 'V' + (y + r) + 'Q' + x + ',' + y + ' ' + (x + r) + ',' + y + 'Z';
  }
  function measure(svg, s) {
    var t = txt(svg, 0, 0, s, { 'class': 'probe' });
    var w = t.getComputedTextLength ? t.getComputedTextLength() : s.length * 7;
    svg.removeChild(t);
    return w;
  }
  function hover(target, title, value) {
    target.setAttribute('tabindex', '0');
    target.setAttribute('aria-label', title + ': ' + value);
    target.addEventListener('pointermove', function (e) { showTip(title, value, e.clientX, e.clientY); });
    target.addEventListener('pointerleave', hideTip);
    target.addEventListener('focus', function () {
      var r = target.getBoundingClientRect();
      showTip(title, value, r.left + r.width / 2, r.top);
    });
    target.addEventListener('blur', hideTip);
  }

  function unitText(spec, v) {
    var u = pick(spec.unit);
    return fmt(v, spec.digits) + (u ? ' ' + u : '');
  }

  function drawHBar(body, spec, width) {
    var rtl = lang === 'ar';
    var rows = spec.rows;
    var series = spec.series ? spec.series.length : 1;
    var bar = series > 1 ? 12 : Math.min(22, spec.barSize || 20);
    var groupGap = series > 1 ? 10 : 8;
    var rowH = bar * series + 2 * (series - 1) + groupGap;
    var svg = el('svg', { role: 'img', direction: 'ltr' }, body);
    var labelW = 0;
    rows.forEach(function (r) { labelW = Math.max(labelW, measure(svg, pick(r.label))); });
    labelW = Math.min(labelW + 12, width * 0.42);
    var valueW = 64;
    var plotW = Math.max(60, width - labelW - valueW);
    var top = 22;
    var height = top + rows.length * rowH + 4;
    svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
    svg.setAttribute('aria-label', pick(spec.title));
    var max = 0;
    rows.forEach(function (r) { (r.values || [r.value]).forEach(function (v) { max = Math.max(max, v); }); });
    max = niceMax(max);
    var x0 = rtl ? valueW : labelW;   // plot area start (left edge in svg coordinates)
    function X(v) { return rtl ? x0 + plotW - (v / max) * plotW : x0 + (v / max) * plotW; }
    // grid + tick labels
    ticks(max, width < 480 ? 3 : 5).forEach(function (t) {
      var x = X(t);
      el('line', { x1: x, x2: x, y1: top - 4, y2: height - 2, 'class': t === 0 ? 'base' : 'grid' }, svg);
      txt(svg, x, 12, fmt(t, 0), { 'text-anchor': 'middle', 'font-size': '11' });
    });
    rows.forEach(function (r, i) {
      var y = top + i * rowH + groupGap / 2;
      var g = el('g', { 'class': 'r' }, svg);
      el('rect', { x: 0, y: y - groupGap / 2 + 1, width: width, height: rowH - 2, rx: 6, 'class': 'row-hl' }, g);
      // Labels sit against the baseline: right-aligned before it in English, mirrored in Arabic.
      var label = pick(r.label);
      txt(g, rtl ? width - labelW + 10 : labelW - 10, y + (bar * series + 2 * (series - 1)) / 2 + 4, label,
        { 'text-anchor': rtl ? 'start' : 'end' });
      var vals = r.values || [r.value];
      vals.forEach(function (v, si) {
        var by = y + si * (bar + 2);
        var w = (v / max) * plotW;
        var x = rtl ? x0 + plotW - w : x0;
        el('path', { d: barPath(x, by, w, bar, rtl ? 'left' : 'right'), 'class': 'mark' + (si ? ' s2' : '') }, g);
        var vx = rtl ? x - 6 : x + w + 6;
        txt(g, vx, by + bar / 2 + 4, unitText(spec, v), { 'text-anchor': rtl ? 'end' : 'start', 'class': 'val', 'font-size': '11.5' });
      });
      var hit = el('rect', { x: 0, y: y - groupGap / 2, width: width, height: rowH, 'class': 'hit' }, g);
      var tv = vals.map(function (v, si) { return (spec.series ? pick(spec.series[si]) + ': ' : '') + unitText(spec, v); }).join(' · ');
      hover(hit, label + (r.note ? ' — ' + pick(r.note) : ''), tv);
    });
  }

  function drawBar(body, spec, width) {
    var rtl = lang === 'ar';
    var rows = rtl ? spec.rows.slice().reverse() : spec.rows;
    var svg = el('svg', { role: 'img', direction: 'ltr', 'aria-label': pick(spec.title) }, body);
    var height = spec.height || 230;
    var left = rtl ? 8 : 34, right = rtl ? 34 : 8, top = 18, bottom = 38;
    var plotW = width - left - right, plotH = height - top - bottom;
    svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
    var max = 0;
    rows.forEach(function (r) { max = Math.max(max, r.value); });
    max = niceMax(max);
    ticks(max, 4).forEach(function (t) {
      var y = top + plotH - (t / max) * plotH;
      el('line', { x1: left, x2: left + plotW, y1: y, y2: y, 'class': t === 0 ? 'base' : 'grid' }, svg);
      txt(svg, rtl ? left + plotW + 6 : left - 6, y + 4, fmt(t, 0), { 'text-anchor': rtl ? 'start' : 'end', 'font-size': '11' });
    });
    var slot = plotW / rows.length;
    var bw = Math.min(24, slot - 2);
    var every = Math.ceil(rows.length / Math.max(1, Math.floor(plotW / 48)));
    rows.forEach(function (r, i) {
      var cx = left + slot * i + slot / 2;
      var h = (r.value / max) * plotH;
      var g = el('g', {}, svg);
      el('path', { d: barPath(cx - bw / 2, top + plotH - h, bw, h, 'up'), 'class': 'mark' }, g);
      if (r.value > 0 && bw >= 14) txt(g, cx, top + plotH - h - 5, fmt(r.value, 0), { 'text-anchor': 'middle', 'class': 'val', 'font-size': '11' });
      if (i % every === 0) txt(svg, cx, top + plotH + 16, pick(r.label), { 'text-anchor': 'middle', 'font-size': '11' });
      if (r.sub && i % every === 0) txt(svg, cx, top + plotH + 30, pick(r.sub), { 'text-anchor': 'middle', 'font-size': '10', fill: 'var(--chart-muted)' });
      var hit = el('rect', { x: cx - slot / 2, y: top, width: slot, height: plotH, 'class': 'hit' }, g);
      hover(hit, pick(r.full || r.label) + (r.sub ? ' ' + pick(r.sub) : ''), unitText(spec, r.value));
    });
  }

  function drawRange(body, spec, width) {
    var rtl = lang === 'ar';
    var rows = spec.rows;
    var svg = el('svg', { role: 'img', direction: 'ltr', 'aria-label': pick(spec.title) }, body);
    var labelW = 0;
    rows.forEach(function (r) { labelW = Math.max(labelW, measure(svg, pick(r.label))); });
    labelW = Math.min(labelW + 14, width * 0.4);
    var pad = 12;
    var plotW = width - labelW - pad * 2;
    var rowH = 34, top = 24;
    var height = top + rows.length * rowH + 4;
    svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
    var max = 0;
    rows.forEach(function (r) { max = Math.max(max, r.max); });
    max = niceMax(max);
    var x0 = rtl ? pad : labelW + pad;
    function X(v) { return rtl ? x0 + plotW - (v / max) * plotW : x0 + (v / max) * plotW; }
    var rt = ticks(max, width < 480 ? 3 : 6);
    rt.forEach(function (t, i) {
      var x = X(t);
      el('line', { x1: x, x2: x, y1: top - 6, y2: height - 2, 'class': t === 0 ? 'base' : 'grid' }, svg);
      txt(svg, x, 12, fmt(t, 0) + (i === rt.length - 1 ? ' ' + pick(spec.unit) : ''), { 'text-anchor': 'middle', 'font-size': '11' });
    });
    rows.forEach(function (r, i) {
      var y = top + i * rowH + 7;
      var g = el('g', { 'class': 'r' }, svg);
      el('rect', { x: 0, y: y - 6, width: width, height: rowH - 2, rx: 6, 'class': 'row-hl' }, g);
      txt(g, rtl ? width - labelW + 4 : labelW - 4, y + 14, pick(r.label), { 'text-anchor': rtl ? 'start' : 'end' });
      var a = X(r.min), b = X(r.max);
      var xs = Math.min(a, b), w = Math.max(6, Math.abs(b - a));
      el('path', { d: pillPath(xs, y + 4, w, 14), 'class': 'mark' }, g);
      var hit = el('rect', { x: 0, y: y - 6, width: width, height: rowH, 'class': 'hit' }, g);
      hover(hit, pick(r.label), fmt(r.min, 0) + '–' + fmt(r.max, 0) + ' ' + pick(spec.unit));
    });
  }

  function drawMatrix(body, spec, width) {
    var rtl = lang === 'ar';
    var cols = spec.cols, rows = spec.rows;
    var svg = el('svg', { role: 'img', direction: 'ltr', 'aria-label': pick(spec.title) }, body);
    var labelW = 0;
    rows.forEach(function (r) { labelW = Math.max(labelW, measure(svg, pick(r.label))); });
    var cell = 20, gapX = 2;
    var colW = Math.max(56, Math.min(110, (width - 160) / cols.length));
    labelW = Math.min(labelW + 16, width - colW * cols.length - 8);
    var gridW = colW * cols.length;
    var head = 30, groupH = 24, rowH = cell + 4;
    var y = head;
    var layout = [];
    var lastGroup = null;
    rows.forEach(function (r) {
      if (r.group && pick(r.group) !== lastGroup) { lastGroup = pick(r.group); layout.push({ group: lastGroup, y: y }); y += groupH; }
      layout.push({ row: r, y: y });
      y += rowH;
    });
    var height = y + 4;
    svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
    var gx = rtl ? width - labelW - gridW : labelW;   // grid left edge
    var colsOrdered = rtl ? cols.map(function (c, i) { return i; }).reverse() : cols.map(function (c, i) { return i; });
    colsOrdered.forEach(function (ci, pos) {
      txt(svg, gx + pos * colW + colW / 2, 18, pick(cols[ci]), { 'text-anchor': 'middle', 'class': 'val', 'font-size': '12' });
    });
    layout.forEach(function (item) {
      if (item.group) {
        txt(svg, rtl ? width - 2 : 2, item.y + 17, item.group, { 'text-anchor': rtl ? 'end' : 'start', 'class': 'val', 'font-size': '12' });
        el('line', { x1: 0, x2: width, y1: item.y + 22, y2: item.y + 22, 'class': 'grid' }, svg);
        return;
      }
      var r = item.row;
      var g = el('g', { 'class': 'r' }, svg);
      el('rect', { x: 0, y: item.y, width: width, height: rowH, rx: 5, 'class': 'row-hl' }, g);
      var label = pick(r.label);
      txt(g, rtl ? width - labelW + 10 : labelW - 10, item.y + rowH / 2 + 4, label, { 'text-anchor': rtl ? 'start' : 'end', 'font-size': '12' });
      colsOrdered.forEach(function (ci, pos) {
        var on = !!r.cells[ci];
        var cx = gx + pos * colW + (colW - cell) / 2;
        el('rect', { x: cx + gapX / 2, y: item.y + 2, width: cell - gapX, height: cell - gapX, rx: 4, 'class': 'cell ' + (on ? 'on' : 'off') }, g);
        var hit = el('rect', { x: gx + pos * colW, y: item.y, width: colW, height: rowH, 'class': 'hit' }, g);
        hover(hit, label + ' · ' + pick(cols[ci]), on ? pick(spec.yes) : pick(spec.no));
      });
    });
  }

  function tableFor(spec) {
    var t = '<table><thead><tr>';
    if (spec.type === 'matrix') {
      t += '<th>' + esc(pick(spec.rowHead)) + '</th>' + spec.cols.map(function (c) { return '<th>' + esc(pick(c)) + '</th>'; }).join('') + '</tr></thead><tbody>';
      spec.rows.forEach(function (r) {
        t += '<tr><td>' + esc(pick(r.label)) + '</td>' + r.cells.map(function (c) { return '<td>' + (c ? '✓' : '—') + '</td>'; }).join('') + '</tr>';
      });
    } else if (spec.type === 'range') {
      t += '<th>' + esc(pick(spec.rowHead)) + '</th><th class="num-cell">' + esc(lang === 'ar' ? 'الأقل' : 'Fastest') + '</th><th class="num-cell">' + esc(lang === 'ar' ? 'الأبطأ' : 'Slowest') + '</th></tr></thead><tbody>';
      spec.rows.forEach(function (r) {
        t += '<tr><td>' + esc(pick(r.label)) + '</td><td class="num-cell">' + fmt(r.min, 0) + ' ' + esc(pick(spec.unit)) + '</td><td class="num-cell">' + fmt(r.max, 0) + ' ' + esc(pick(spec.unit)) + '</td></tr>';
      });
    } else {
      var heads = spec.series ? spec.series.map(pick) : [pick(spec.valueHead) || pick(spec.unit)];
      t += '<th>' + esc(pick(spec.rowHead)) + '</th>' + heads.map(function (h) { return '<th class="num-cell">' + esc(h) + '</th>'; }).join('') + '</tr></thead><tbody>';
      spec.rows.forEach(function (r) {
        var vals = r.values || [r.value];
        t += '<tr><td>' + esc(pick(r.full || r.label)) + (r.sub ? ' ' + esc(pick(r.sub)) : '') + '</td>' + vals.map(function (v) { return '<td class="num-cell">' + fmt(v, spec.digits) + '</td>'; }).join('') + '</tr>';
      });
    }
    return t + '</tbody></table>';
  }

  function drawChart(fig) {
    var script = fig.querySelector('script[type="application/json"]');
    if (!script) return;
    var spec;
    try { spec = JSON.parse(script.textContent); } catch (e) { fig.textContent = 'Chart data could not be read.'; return; }
    var old = fig.querySelector('.chart-render');
    if (old) fig.removeChild(old);
    var wrap = document.createElement('div');
    wrap.className = 'chart-render';
    var h = '<div class="chart-title">' + esc(pick(spec.title)) + '</div>';
    if (spec.sub) h += '<div class="chart-sub">' + esc(pick(spec.sub)) + '</div>';
    if (spec.series && spec.series.length > 1) {
      h += '<div class="legend">' + spec.series.map(function (s, i) {
        return '<span><i style="background:var(--series-' + (i + 1) + ')"></i>' + esc(pick(s)) + '</span>';
      }).join('') + '</div>';
    }
    if (spec.type === 'matrix') {
      h += '<div class="legend"><span><i style="background:var(--series-1)"></i>' + esc(pick(spec.yes)) + '</span>' +
        '<span><i style="background:var(--chart-grid)"></i>' + esc(pick(spec.no)) + '</span></div>';
    }
    wrap.innerHTML = h + '<div class="chart-body"></div>' +
      '<details class="as-table"><summary>' + (lang === 'ar' ? 'اعرضه جدولًا' : 'Show as a table') + '</summary><div class="table-wrap">' + tableFor(spec) + '</div></details>' +
      (spec.caption ? '<figcaption>' + esc(pick(spec.caption)) + '</figcaption>' : '');
    fig.insertBefore(wrap, script);
    var body = wrap.querySelector('.chart-body');
    var width = Math.max(260, Math.floor(body.clientWidth || fig.clientWidth - 36));
    if (spec.type === 'hbar') drawHBar(body, spec, width);
    else if (spec.type === 'bar') drawBar(body, spec, width);
    else if (spec.type === 'range') drawRange(body, spec, width);
    else if (spec.type === 'matrix') drawMatrix(body, spec, width);
  }

  function drawCharts() {
    var figs = document.querySelectorAll('figure.chart');
    for (var i = 0; i < figs.length; i++) {
      try { drawChart(figs[i]); } catch (e) { if (window.console) console.error('chart', e); }
    }
  }

  /* ------------------------------------------------------------------ start */

  function afterLangChange() {
    updateThemeLabel();
    buildToc();
    drawCharts();
    var btns = document.querySelectorAll('.copy-btn');
    for (var i = 0; i < btns.length; i++) btns[i].textContent = lang === 'ar' ? 'نسخ' : 'Copy';
    var titles = document.querySelectorAll('[data-title-' + lang + ']');
    if (titles.length) document.title = titles[0].getAttribute('data-title-' + lang);
    else {
      var t = document.body.getAttribute('data-title-' + lang);
      if (t) document.title = t;
    }
  }

  function start() {
    buildShell();
    addAnchors();
    addCopyButtons();
    setupFilters();
    afterLangChange();
    var w = window.innerWidth, timer;
    window.addEventListener('resize', function () {
      if (Math.abs(window.innerWidth - w) < 8) return;
      w = window.innerWidth;
      clearTimeout(timer);
      timer = setTimeout(drawCharts, 150);
    });
    if (window.matchMedia) {
      var mq = window.matchMedia('(prefers-color-scheme: dark)');
      if (mq.addEventListener) mq.addEventListener('change', updateThemeLabel);
    }
    if (window.location.hash) {
      var target = document.getElementById(window.location.hash.slice(1));
      if (target) setTimeout(function () { target.scrollIntoView(); }, 0);
    }
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
