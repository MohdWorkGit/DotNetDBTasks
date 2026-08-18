#!/usr/bin/env node
/**
 * Rebuilds the demo dataset the user manual is illustrated with.
 *
 * The manual's screenshots are only as convincing as the data behind them, and a
 * development database drifts: queries named "test2", tasks named "tst", accounts
 * left behind by e2e runs. This script puts the Bayan side of the demo back to a
 * known, plausible state — a small company running sales, customer service,
 * inventory, finance and HR reports over the schema created by
 * `database/demo-data.sql`.
 *
 * ## Why it takes a language
 *
 * A query, a query group and a user group each carry ONE name and ONE description —
 * the application does not translate content an administrator typed. So the Arabic
 * manual can only show Arabic query names if the demo data is in Arabic when its
 * screenshots are taken. The manual is therefore captured in two passes, re-seeding
 * in between:
 *
 *     node seed-demo-data.js --lang ar && node capture-screenshots.js --locales ar
 *     node seed-demo-data.js --lang en && node capture-screenshots.js --locales en
 *
 * `npm run manual` does exactly that and then builds both documents. Every name,
 * description, parameter label and drop-down option below is written once as
 * T("English", "العربية"), so the two passes cannot drift apart.
 *
 * Drop-down *values* stay English in both passes — they are compared against the
 * data by the SQL ('Confirmed', 'ALL'); only the labels change.
 *
 * Everything goes through the REST API rather than SQL, so the rows land with the
 * same validation, derived query types and audit entries as if an administrator had
 * typed them in.
 *
 * DESTRUCTIVE. It deletes every scheduled task, query and group in the target
 * instance before creating its own (deleting a query cascades to its execution
 * logs). It is meant for the demo/manual database — do not point it at an instance
 * whose data matters.
 *
 * Config, all optional:
 *   --lang en|ar       language of the seeded content (default en)
 *   MANUAL_API_URL     default http://localhost:60187/api
 *   MANUAL_ADMIN_USER  default admin
 *   MANUAL_ADMIN_PASS  default Admin@123
 *   MANUAL_DEMO_PASS   default Demo@123   (password given to the demo accounts)
 *   MANUAL_TASK_FOLDER default C:\Bayan\exports\sales
 */

const argv = process.argv.slice(2);
const argOf = (name) => {
  const i = argv.indexOf(`--${name}`);
  return i >= 0 ? argv[i + 1] : undefined;
};

const LANG = (argOf('lang') || process.env.MANUAL_SEED_LANG || 'en').toLowerCase();
if (!['en', 'ar'].includes(LANG)) throw new Error(`--lang must be en or ar, got '${LANG}'`);

/** The one place a string is written in both languages. */
const T = (en, ar) => (LANG === 'ar' ? ar : en);

const API = process.env.MANUAL_API_URL || 'http://localhost:60187/api';
const ADMIN_USER = process.env.MANUAL_ADMIN_USER || 'admin';
const ADMIN_PASS = process.env.MANUAL_ADMIN_PASS || 'Admin@123';
const DEMO_PASS = process.env.MANUAL_DEMO_PASS || 'Demo@123';
const TASK_FOLDER = process.env.MANUAL_TASK_FOLDER || 'C:\\Bayan\\exports\\sales';

const log = (...a) => console.log('[seed]', ...a);

// ---------------------------------------------------------------- http

let token = null;

async function call(method, path, body, { raw = false } = {}) {
  const res = await fetch(API + path, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: 'Bearer ' + token } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`${method} ${path} → ${res.status} ${text.slice(0, 400)}`);
  }
  if (raw || res.status === 204) return null;
  return res.json().catch(() => null);
}

const get = (p) => call('GET', p);
const post = (p, b) => call('POST', p, b);
const del = (p) => call('DELETE', p, undefined, { raw: true });
const unwrap = (j) => (j && j.items) || j || [];
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

/**
 * Signing in eleven demo accounts in a row trips the login rate limiter
 * (Auth:Login — ten attempts a minute by default), so tokens are reused and a 429
 * is waited out rather than treated as a failure.
 */
const tokenCache = new Map();

async function loginAs(username, password) {
  if (tokenCache.has(username)) return tokenCache.get(username);

  for (let attempt = 1; ; attempt++) {
    const res = await fetch(`${API}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    });
    if (res.ok) {
      const accessToken = (await res.json()).accessToken;
      tokenCache.set(username, accessToken);
      return accessToken;
    }
    if (res.status !== 429 || attempt > 3)
      throw new Error(`login failed for ${username} (${res.status})`);
    log(`login rate limited, waiting 60s before retrying ${username}…`);
    await sleep(61_000);
  }
}

// ---------------------------------------------------------------- demo content
//
// Parameters are written as @name: the executor rewrites them to :name for Oracle,
// and multi-value parameters are only expanded in that form. SQL must not contain
// ';' or '--' — CreateDynamicQueryValidator rejects both.
//
// Everything is cross-referenced by `key`, never by name, so the access rules and
// the seeded history survive the switch between languages.

const EXPORTS = ['Excel', 'Csv', 'Pdf', 'Word'];

/** label/value option list for a Static drop-down. Labels translate, values do not. */
const opts = (...pairs) => JSON.stringify(pairs.map(([label, value]) => ({ label, value })));

const P = {
  date: (name, displayName, sortOrder, isRequired = true) =>
    ({ name, displayName, parameterType: 2, isRequired, sortOrder }),
  number: (name, displayName, sortOrder, isRequired = true, defaultValue = null) =>
    ({ name, displayName, parameterType: 1, isRequired, sortOrder, defaultValue }),
  text: (name, displayName, sortOrder, isRequired = true) =>
    ({ name, displayName, parameterType: 0, isRequired, sortOrder }),
  bool: (name, displayName, sortOrder, defaultValue = 'true') =>
    ({ name, displayName, parameterType: 3, isRequired: false, sortOrder, defaultValue }),
  /** Drop-down fed by a fixed list. */
  list: (name, displayName, sortOrder, staticValues, defaultValue = null) =>
    ({ name, displayName, parameterType: 4, isRequired: true, sortOrder,
       dropdownSourceType: 0, dropdownStaticValues: staticValues, defaultValue }),
  /** Drop-down fed by another query — `lookup` is resolved to its id at creation time. */
  lookup: (name, displayName, sortOrder, lookup, valueColumn, labelColumn, allowMultiple = false) =>
    ({ name, displayName, parameterType: 4, isRequired: true, sortOrder, allowMultiple,
       dropdownSourceType: 1, lookup, dropdownQueryValueColumn: valueColumn,
       dropdownQueryLabelColumn: labelColumn })
};

const QUERY_GROUPS = [
  { key: 'sales',     name: T('Sales Reports', 'تقارير المبيعات'),
    description: T('Order, revenue and sales-performance reports for the sales team.',
                   'تقارير الطلبات والإيرادات وأداء المبيعات لفريق المبيعات.') },
  { key: 'service',   name: T('Customer Service', 'خدمة العملاء'),
    description: T('Customer lookups and the day-to-day corrections the service desk makes.',
                   'استعلامات العملاء والتصحيحات اليومية التي يجريها مكتب الخدمة.') },
  { key: 'inventory', name: T('Inventory and Stock', 'المخزون والأصناف'),
    description: T('Stock levels, catalogue listings and stock corrections.',
                   'مستويات المخزون وقوائم الأصناف وتعديلات الكميات.') },
  { key: 'finance',   name: T('Finance and Invoicing', 'المالية والفواتير'),
    description: T('Receivables, invoice ageing and payment recording.',
                   'الذمم المدينة وأعمار الفواتير وتسجيل المدفوعات.') },
  { key: 'hr',        name: T('Human Resources', 'الموارد البشرية'),
    description: T('Employee directory, headcount and joiner reports.',
                   'دليل الموظفين وأعداد القوى العاملة وتقارير التعيينات الجديدة.') },
  { key: 'reference', name: T('Reference Lists', 'القوائم المرجعية'),
    description: T('Small lists that feed the drop-down parameters of the other queries.',
                   'قوائم صغيرة تغذّي قوائم الاختيار في معايير البحث للاستعلامات الأخرى.') }
];

const USER_GROUPS = [
  { key: 'salesTeam',  name: T('Sales Team', 'فريق المبيعات'),
    description: T('Sales managers and representatives.', 'مديرو ومندوبو المبيعات.'),
    members: ['n.alqahtani', 'k.rahman', 'm.zaid'] },
  { key: 'serviceDesk', name: T('Customer Service Desk', 'مكتب خدمة العملاء'),
    description: T('Agents handling customer calls and corrections.',
                   'الموظفون الذين يتعاملون مع مكالمات العملاء وتصحيح بياناتهم.'),
    members: ['l.haddad', 'o.mansour'] },
  { key: 'warehouse',  name: T('Warehouse and Inventory', 'المستودع والمخزون'),
    description: T('Warehouse supervisors and inventory controllers.',
                   'مشرفو المستودع ومراقبو المخزون.'),
    members: ['t.ibrahim', 'f.said'] },
  { key: 'financeTeam', name: T('Finance Team', 'فريق المالية'),
    description: T('Receivables and accounting staff.', 'موظفو الذمم المدينة والمحاسبة.'),
    members: ['y.aziz', 's.farid'] },
  { key: 'hrTeam',     name: T('HR Team', 'فريق الموارد البشرية'),
    description: T('Human-resources officers.', 'موظفو الموارد البشرية.'),
    members: ['h.nasser', 'z.kamal'] }
];

const DEMO_USERS = [
  ['n.alqahtani', 'Nora', 'Al-Qahtani', 'nora.q@example.com'],
  ['k.rahman', 'Khalid', 'Rahman', 'khalid.r@example.com'],
  ['m.zaid', 'Maha', 'Zaid', 'maha.z@example.com'],
  ['l.haddad', 'Layla', 'Haddad', 'layla.h@example.com'],
  ['o.mansour', 'Omar', 'Mansour', 'omar.m@example.com'],
  ['t.ibrahim', 'Tarek', 'Ibrahim', 'tarek.i@example.com'],
  ['f.said', 'Fatima', 'Said', 'fatima.s@example.com'],
  ['y.aziz', 'Yousef', 'Aziz', 'yousef.a@example.com'],
  ['s.farid', 'Salma', 'Farid', 'salma.f@example.com'],
  ['h.nasser', 'Huda', 'Nasser', 'huda.n@example.com'],
  ['z.kamal', 'Ziad', 'Kamal', 'ziad.k@example.com']
];

// The lookup queries have to exist before the queries whose drop-downs read them.
const LOOKUPS = [
  {
    key: 'regions',
    name: T('Lookup: Regions', 'قائمة: المناطق'),
    description: T('The sales regions, feeding the region drop-downs.',
                   'المناطق البيعية، وتغذّي قوائم اختيار المنطقة.'),
    group: 'reference',
    sql: `SELECT DISTINCT c.REGION AS REGION_CODE,
                c.REGION AS REGION_NAME
  FROM CUSTOMERS c
 ORDER BY 1`
  },
  {
    key: 'departments',
    name: T('Lookup: Departments', 'قائمة: الأقسام'),
    description: T('Company departments, feeding the department drop-downs.',
                   'أقسام الشركة، وتغذّي قوائم اختيار القسم.'),
    group: 'reference',
    sql: `SELECT d.DEPT_ID,
       d.DEPT_NAME
  FROM DEPARTMENTS d
 ORDER BY d.DEPT_NAME`
  },
  {
    key: 'categories',
    name: T('Lookup: Product Categories', 'قائمة: فئات الأصناف'),
    description: T('Product categories, feeding the catalogue drop-downs.',
                   'فئات الأصناف، وتغذّي قوائم اختيار الفئة.'),
    group: 'reference',
    sql: `SELECT DISTINCT p.CATEGORY AS CATEGORY_CODE,
                p.CATEGORY AS CATEGORY_NAME
  FROM PRODUCTS p
 ORDER BY 1`
  },
  {
    key: 'customers',
    name: T('Lookup: Active Customers', 'قائمة: العملاء النشطون'),
    description: T('Active customer accounts, feeding the customer drop-downs.',
                   'حسابات العملاء النشطة، وتغذّي قوائم اختيار العميل.'),
    group: 'reference',
    sql: `SELECT c.CUSTOMER_ID,
       c.CUSTOMER_NAME
  FROM CUSTOMERS c
 WHERE c.IS_ACTIVE = 1
 ORDER BY c.CUSTOMER_NAME`
  }
];

const STATUS_OPTIONS = opts(
  [T('All statuses', 'كل الحالات'), 'ALL'],
  [T('New', 'جديد'), 'New'],
  [T('Confirmed', 'مؤكد'), 'Confirmed'],
  [T('Shipped', 'تم الشحن'), 'Shipped'],
  [T('Delivered', 'تم التسليم'), 'Delivered'],
  [T('Cancelled', 'ملغي'), 'Cancelled']
);

const EMP_STATUS_OPTIONS = opts(
  [T('All', 'الكل'), 'ALL'],
  [T('Active', 'على رأس العمل'), 'Active'],
  [T('On Leave', 'في إجازة'), 'On Leave'],
  [T('Resigned', 'مستقيل'), 'Resigned']
);

const QUERIES = [
  // ---------------------------------------------------------- sales reports
  {
    key: 'ordersByPeriod',
    name: T('Orders by Period and Status', 'الطلبات حسب الفترة والحالة'),
    description: T('All orders raised in a date range, filtered by status and minimum value. The everyday sales report.',
                   'جميع الطلبات المسجّلة خلال فترة محددة، مع تصفيتها بالحالة والحد الأدنى للقيمة. تقرير المبيعات اليومي.'),
    group: 'sales',
    exports: EXPORTS,
    sql: `SELECT o.ORDER_NO,
       c.CUSTOMER_NAME,
       c.CUSTOMER_NAME_AR,
       o.ORDER_DATE,
       o.ORDER_STATUS,
       o.PAYMENT_METHOD,
       o.REGION,
       o.TOTAL_AMOUNT,
       o.CURRENCY,
       e.FULL_NAME AS SALES_REP
  FROM ORDERS o
  JOIN CUSTOMERS c ON c.CUSTOMER_ID = o.CUSTOMER_ID
  JOIN EMPLOYEES e ON e.EMP_ID = o.SALES_REP_ID
 WHERE o.ORDER_DATE >= @FromDate
   AND o.ORDER_DATE <= @ToDate
   AND o.TOTAL_AMOUNT >= @MinTotal
   AND (@OrderStatus = 'ALL' OR o.ORDER_STATUS = @OrderStatus)
 ORDER BY o.ORDER_DATE DESC`,
    parameters: [
      P.date('FromDate', T('Order date from', 'من تاريخ الطلب'), 1),
      P.date('ToDate', T('Order date to', 'إلى تاريخ الطلب'), 2),
      P.number('MinTotal', T('Minimum order value', 'الحد الأدنى لقيمة الطلب'), 3, false, '0'),
      P.list('OrderStatus', T('Order status', 'حالة الطلب'), 4, STATUS_OPTIONS, 'ALL')
    ]
  },
  {
    key: 'salesByRegion',
    name: T('Sales by Region', 'المبيعات حسب المنطقة'),
    description: T('Order count, total and average order value per region. Pick one region or several.',
                   'عدد الطلبات وإجمالي ومتوسط قيمتها لكل منطقة. يمكن اختيار منطقة واحدة أو عدة مناطق.'),
    group: 'sales',
    exports: EXPORTS,
    sql: `SELECT o.REGION,
       COUNT(*) AS ORDER_COUNT,
       SUM(o.TOTAL_AMOUNT) AS TOTAL_SALES,
       ROUND(AVG(o.TOTAL_AMOUNT), 2) AS AVERAGE_ORDER
  FROM ORDERS o
 WHERE o.REGION IN (@Regions)
   AND o.ORDER_DATE BETWEEN @FromDate AND @ToDate
   AND o.ORDER_STATUS <> 'Cancelled'
 GROUP BY o.REGION
 ORDER BY TOTAL_SALES DESC`,
    parameters: [
      P.lookup('Regions', T('Regions', 'المناطق'), 1, 'regions', 'REGION_CODE', 'REGION_NAME', true),
      P.date('FromDate', T('Order date from', 'من تاريخ الطلب'), 2),
      P.date('ToDate', T('Order date to', 'إلى تاريخ الطلب'), 3)
    ]
  },
  {
    key: 'topCustomers',
    name: T('Top Customers by Revenue', 'أكبر العملاء من حيث الإيرادات'),
    description: T('The highest-spending customers in a period, largest first.',
                   'العملاء الأعلى إنفاقًا خلال فترة محددة، مرتّبين تنازليًا.'),
    group: 'sales',
    exports: EXPORTS,
    sql: `SELECT *
  FROM (SELECT c.CUSTOMER_CODE,
               c.CUSTOMER_NAME,
               c.SEGMENT,
               COUNT(o.ORDER_ID) AS ORDER_COUNT,
               SUM(o.TOTAL_AMOUNT) AS TOTAL_REVENUE
          FROM CUSTOMERS c
          JOIN ORDERS o ON o.CUSTOMER_ID = c.CUSTOMER_ID
         WHERE o.ORDER_DATE BETWEEN @FromDate AND @ToDate
           AND o.ORDER_STATUS <> 'Cancelled'
         GROUP BY c.CUSTOMER_CODE, c.CUSTOMER_NAME, c.SEGMENT
         ORDER BY TOTAL_REVENUE DESC)
 WHERE ROWNUM <= @TopCount`,
    parameters: [
      P.date('FromDate', T('Order date from', 'من تاريخ الطلب'), 1),
      P.date('ToDate', T('Order date to', 'إلى تاريخ الطلب'), 2),
      P.number('TopCount', T('How many customers', 'عدد العملاء'), 3, true, '10')
    ]
  },
  {
    key: 'orderDetails',
    name: T('Order Details by Order Number', 'تفاصيل الطلب برقم الطلب'),
    description: T('The lines of a single order — product, quantity, unit price and line total.',
                   'بنود طلب واحد: الصنف والكمية وسعر الوحدة وإجمالي البند.'),
    group: 'sales',
    exports: ['Excel', 'Pdf', 'Word'],
    sql: `SELECT o.ORDER_NO,
       p.SKU,
       p.PRODUCT_NAME,
       p.PRODUCT_NAME_AR,
       i.QUANTITY,
       i.UNIT_PRICE,
       i.LINE_TOTAL
  FROM ORDER_ITEMS i
  JOIN ORDERS o ON o.ORDER_ID = i.ORDER_ID
  JOIN PRODUCTS p ON p.PRODUCT_ID = i.PRODUCT_ID
 WHERE o.ORDER_NO = @OrderNo
 ORDER BY i.ITEM_ID`,
    parameters: [P.text('OrderNo', T('Order number', 'رقم الطلب'), 1)]
  },
  {
    key: 'dailySales',
    name: T('Daily Sales Summary', 'ملخص المبيعات اليومي'),
    description: T('Orders and revenue per day for the last 30 days. Exported every morning by the scheduled task.',
                   'عدد الطلبات والإيرادات لكل يوم خلال آخر 30 يومًا. تُصدّره المهمة المجدولة كل صباح.'),
    group: 'sales',
    exports: EXPORTS,
    sql: `SELECT TRUNC(o.ORDER_DATE) AS ORDER_DAY,
       COUNT(*) AS ORDER_COUNT,
       SUM(o.TOTAL_AMOUNT) AS TOTAL_SALES
  FROM ORDERS o
 WHERE o.ORDER_DATE >= TRUNC(SYSDATE) - 30
   AND o.ORDER_STATUS <> 'Cancelled'
 GROUP BY TRUNC(o.ORDER_DATE)
 ORDER BY ORDER_DAY DESC`,
    parameters: []
  },

  // ------------------------------------------------------- customer service
  {
    key: 'customerDirectory',
    name: T('Customer Directory', 'دليل العملاء'),
    description: T('Contact details for customers, searchable by name and region.',
                   'بيانات التواصل مع العملاء، مع إمكانية البحث بالاسم والمنطقة.'),
    group: 'service',
    exports: ['Excel', 'Csv'],
    sql: `SELECT c.CUSTOMER_CODE,
       c.CUSTOMER_NAME,
       c.CUSTOMER_NAME_AR,
       c.CITY,
       c.REGION,
       c.SEGMENT,
       c.CONTACT_NAME,
       c.CONTACT_EMAIL,
       c.CONTACT_PHONE
  FROM CUSTOMERS c
 WHERE UPPER(c.CUSTOMER_NAME) LIKE '%' || UPPER(@NameContains) || '%'
   AND c.REGION = @Region
 ORDER BY c.CUSTOMER_NAME`,
    parameters: [
      P.text('NameContains', T('Name contains', 'الاسم يحتوي على'), 1, false),
      P.lookup('Region', T('Region', 'المنطقة'), 2, 'regions', 'REGION_CODE', 'REGION_NAME')
    ]
  },
  {
    key: 'customerHistory',
    name: T('Customer Order History', 'سجل طلبات العميل'),
    description: T('Every order a single customer has placed, newest first.',
                   'جميع الطلبات التي سجّلها عميل واحد، الأحدث أولًا.'),
    group: 'service',
    exports: ['Excel', 'Csv', 'Pdf'],
    sql: `SELECT o.ORDER_NO,
       o.ORDER_DATE,
       o.ORDER_STATUS,
       o.TOTAL_AMOUNT,
       o.PAYMENT_METHOD,
       o.DELIVERY_DATE
  FROM ORDERS o
  JOIN CUSTOMERS c ON c.CUSTOMER_ID = o.CUSTOMER_ID
 WHERE c.CUSTOMER_CODE = @CustomerCode
 ORDER BY o.ORDER_DATE DESC`,
    parameters: [P.text('CustomerCode', T('Customer code', 'رمز العميل'), 1)]
  },
  {
    key: 'updateCustomerPhone',
    name: T('Update Customer Contact Phone', 'تحديث هاتف العميل'),
    description: T('Corrects the contact telephone number held against a customer account.',
                   'تصحيح رقم هاتف التواصل المسجّل على حساب العميل.'),
    group: 'service',
    exports: [],
    sql: `UPDATE CUSTOMERS
   SET CONTACT_PHONE = @NewPhone
 WHERE CUSTOMER_ID = @CustomerId`,
    parameters: [
      P.lookup('CustomerId', T('Customer', 'العميل'), 1, 'customers', 'CUSTOMER_ID', 'CUSTOMER_NAME'),
      P.text('NewPhone', T('New telephone number', 'رقم الهاتف الجديد'), 2)
    ]
  },
  {
    key: 'deactivateCustomer',
    name: T('Deactivate Customer Account', 'إيقاف حساب عميل'),
    description: T('Marks a customer account inactive so it stops appearing in the directory.',
                   'تعليم حساب العميل كغير نشط فيتوقف ظهوره في الدليل.'),
    group: 'service',
    exports: [],
    sql: `UPDATE CUSTOMERS
   SET IS_ACTIVE = 0
 WHERE CUSTOMER_ID = @CustomerId`,
    parameters: [
      P.lookup('CustomerId', T('Customer', 'العميل'), 1, 'customers', 'CUSTOMER_ID', 'CUSTOMER_NAME')
    ]
  },

  // ---------------------------------------------------------- inventory
  {
    key: 'lowStock',
    name: T('Stock Below Reorder Level', 'الأصناف تحت حد إعادة الطلب'),
    description: T('Active products whose stock has fallen under their reorder level, worst shortfall first.',
                   'الأصناف النشطة التي انخفض مخزونها دون حد إعادة الطلب، الأشد نقصًا أولًا.'),
    group: 'inventory',
    exports: EXPORTS,
    sql: `SELECT p.SKU,
       p.PRODUCT_NAME,
       p.PRODUCT_NAME_AR,
       p.CATEGORY,
       p.STOCK_QTY,
       p.REORDER_LEVEL,
       p.REORDER_LEVEL - p.STOCK_QTY AS SHORTFALL
  FROM PRODUCTS p
 WHERE p.STOCK_QTY < p.REORDER_LEVEL
   AND p.IS_ACTIVE = 1
 ORDER BY SHORTFALL DESC`,
    parameters: []
  },
  {
    key: 'catalogue',
    name: T('Product Catalogue by Category', 'دليل الأصناف حسب الفئة'),
    description: T('Price and stock for every product in a category.',
                   'السعر والمخزون لكل صنف ضمن فئة محددة.'),
    group: 'inventory',
    exports: EXPORTS,
    sql: `SELECT p.SKU,
       p.PRODUCT_NAME,
       p.PRODUCT_NAME_AR,
       p.CATEGORY,
       p.UNIT_PRICE,
       p.STOCK_QTY,
       p.REORDER_LEVEL,
       p.IS_ACTIVE
  FROM PRODUCTS p
 WHERE p.CATEGORY = @Category
   AND (@IncludeDiscontinued = 1 OR p.IS_ACTIVE = 1)
 ORDER BY p.PRODUCT_NAME`,
    parameters: [
      P.lookup('Category', T('Category', 'الفئة'), 1, 'categories', 'CATEGORY_CODE', 'CATEGORY_NAME'),
      P.bool('IncludeDiscontinued', T('Include discontinued products', 'تضمين الأصناف الموقوفة'), 2, 'false')
    ]
  },
  {
    key: 'adjustStock',
    name: T('Adjust Product Stock Level', 'تعديل كمية مخزون صنف'),
    description: T('Sets the counted stock quantity and reorder level of a product after a stock take.',
                   'تحديث الكمية المجرودة وحد إعادة الطلب لصنف بعد الجرد.'),
    group: 'inventory',
    exports: [],
    sql: `UPDATE PRODUCTS
   SET STOCK_QTY = @NewStockQty,
       REORDER_LEVEL = @NewReorderLevel
 WHERE PRODUCT_ID = @ProductId`,
    parameters: [
      P.number('ProductId', T('Product id', 'رقم الصنف'), 1),
      P.number('NewStockQty', T('Counted stock quantity', 'الكمية المجرودة'), 2),
      P.number('NewReorderLevel', T('Reorder level', 'حد إعادة الطلب'), 3)
    ]
  },
  {
    key: 'slowMoving',
    name: T('Slow Moving Products', 'الأصناف بطيئة الحركة'),
    description: T('Products with no sale for a given number of months, oldest sale first.',
                   'الأصناف التي لم تُبَع منذ عدد محدد من الأشهر، الأقدم أولًا.'),
    group: 'inventory',
    exports: ['Excel', 'Csv'],
    sql: `SELECT p.SKU,
       p.PRODUCT_NAME,
       p.CATEGORY,
       p.STOCK_QTY,
       MAX(o.ORDER_DATE) AS LAST_SOLD_ON
  FROM PRODUCTS p
  LEFT JOIN ORDER_ITEMS i ON i.PRODUCT_ID = p.PRODUCT_ID
  LEFT JOIN ORDERS o ON o.ORDER_ID = i.ORDER_ID
 GROUP BY p.SKU, p.PRODUCT_NAME, p.CATEGORY, p.STOCK_QTY
HAVING NVL(MAX(o.ORDER_DATE), SYSDATE - 9999) < SYSDATE - (@Months * 30)
 ORDER BY LAST_SOLD_ON`,
    parameters: [P.number('Months', T('Months without a sale', 'عدد الأشهر بدون بيع'), 1, true, '3')]
  },

  // ------------------------------------------------------------- finance
  {
    key: 'outstandingInvoices',
    name: T('Outstanding Invoices', 'الفواتير المستحقة'),
    description: T('Unpaid invoices due before a chosen date, with the balance still owed.',
                   'الفواتير غير المسددة المستحقة قبل تاريخ محدد، مع الرصيد المتبقي.'),
    group: 'finance',
    exports: EXPORTS,
    sql: `SELECT i.INVOICE_NO,
       c.CUSTOMER_NAME,
       i.ISSUE_DATE,
       i.DUE_DATE,
       i.AMOUNT,
       i.PAID_AMOUNT,
       i.AMOUNT - i.PAID_AMOUNT AS BALANCE,
       i.INVOICE_STATUS
  FROM INVOICES i
  JOIN CUSTOMERS c ON c.CUSTOMER_ID = i.CUSTOMER_ID
 WHERE i.INVOICE_STATUS <> 'Paid'
   AND i.DUE_DATE <= @DueBefore
   AND i.AMOUNT >= @MinAmount
 ORDER BY i.DUE_DATE`,
    parameters: [
      P.date('DueBefore', T('Due on or before', 'مستحقة في أو قبل'), 1),
      P.number('MinAmount', T('Minimum invoice amount', 'الحد الأدنى لقيمة الفاتورة'), 2, false, '0')
    ]
  },
  {
    key: 'invoiceAgeing',
    name: T('Invoice Ageing by Customer', 'أعمار فواتير العميل'),
    description: T('How overdue each unpaid invoice of one customer is.',
                   'مدة تأخر كل فاتورة غير مسددة لعميل واحد.'),
    group: 'finance',
    exports: ['Excel', 'Pdf', 'Word'],
    sql: `SELECT i.INVOICE_NO,
       i.ISSUE_DATE,
       i.DUE_DATE,
       TRUNC(SYSDATE - i.DUE_DATE) AS DAYS_OVERDUE,
       i.AMOUNT,
       i.AMOUNT - i.PAID_AMOUNT AS BALANCE,
       i.INVOICE_STATUS
  FROM INVOICES i
  JOIN CUSTOMERS c ON c.CUSTOMER_ID = i.CUSTOMER_ID
 WHERE c.CUSTOMER_CODE = @CustomerCode
   AND i.INVOICE_STATUS <> 'Paid'
 ORDER BY DAYS_OVERDUE DESC`,
    parameters: [P.text('CustomerCode', T('Customer code', 'رمز العميل'), 1)]
  },
  {
    key: 'recordPayment',
    name: T('Record Invoice Payment', 'تسجيل سداد فاتورة'),
    description: T('Records the amount received against an invoice and settles it.',
                   'تسجيل المبلغ المستلم على الفاتورة وإقفالها.'),
    group: 'finance',
    exports: [],
    sql: `UPDATE INVOICES
   SET PAID_AMOUNT = @PaidAmount,
       INVOICE_STATUS = 'Paid'
 WHERE INVOICE_ID = @InvoiceId`,
    parameters: [
      P.number('InvoiceId', T('Invoice id', 'رقم الفاتورة'), 1),
      P.number('PaidAmount', T('Amount received', 'المبلغ المستلم'), 2)
    ]
  },

  // ------------------------------------------------------------------ hr
  {
    key: 'employeeDirectory',
    name: T('Employee Directory by Department', 'دليل موظفي القسم'),
    description: T('Staff in one department with their job title and contact details.',
                   'موظفو قسم واحد مع المسمى الوظيفي وبيانات التواصل.'),
    group: 'hr',
    exports: ['Excel', 'Csv', 'Pdf'],
    sql: `SELECT e.EMP_NO,
       e.FULL_NAME,
       e.FULL_NAME_AR,
       d.DEPT_NAME,
       e.JOB_TITLE,
       e.EMAIL,
       e.PHONE,
       e.HIRE_DATE,
       e.EMP_STATUS
  FROM EMPLOYEES e
  JOIN DEPARTMENTS d ON d.DEPT_ID = e.DEPT_ID
 WHERE e.DEPT_ID = @Department
   AND (@Status = 'ALL' OR e.EMP_STATUS = @Status)
 ORDER BY e.FULL_NAME`,
    parameters: [
      P.lookup('Department', T('Department', 'القسم'), 1, 'departments', 'DEPT_ID', 'DEPT_NAME'),
      P.list('Status', T('Employment status', 'حالة التوظيف'), 2, EMP_STATUS_OPTIONS, 'ALL')
    ]
  },
  {
    key: 'newHires',
    name: T('New Hires by Period', 'الموظفون الجدد خلال فترة'),
    description: T('Everyone who joined between two dates, newest joiner first.',
                   'الموظفون الذين التحقوا بالعمل بين تاريخين، الأحدث أولًا.'),
    group: 'hr',
    exports: ['Excel', 'Csv', 'Pdf'],
    sql: `SELECT e.EMP_NO,
       e.FULL_NAME,
       e.FULL_NAME_AR,
       d.DEPT_NAME,
       e.JOB_TITLE,
       e.HIRE_DATE
  FROM EMPLOYEES e
  JOIN DEPARTMENTS d ON d.DEPT_ID = e.DEPT_ID
 WHERE e.HIRE_DATE BETWEEN @FromDate AND @ToDate
 ORDER BY e.HIRE_DATE DESC`,
    parameters: [
      P.date('FromDate', T('Hired from', 'تاريخ التعيين من'), 1),
      P.date('ToDate', T('Hired to', 'تاريخ التعيين إلى'), 2)
    ]
  },
  {
    key: 'headcount',
    name: T('Headcount by Department', 'أعداد الموظفين حسب القسم'),
    description: T('Active headcount and average salary per department.',
                   'عدد الموظفين على رأس العمل ومتوسط الراتب لكل قسم.'),
    group: 'hr',
    exports: EXPORTS,
    sql: `SELECT d.DEPT_CODE,
       d.DEPT_NAME,
       d.DEPT_NAME_AR,
       COUNT(e.EMP_ID) AS HEADCOUNT,
       ROUND(AVG(e.MONTHLY_SALARY), 2) AS AVERAGE_SALARY
  FROM DEPARTMENTS d
  LEFT JOIN EMPLOYEES e ON e.DEPT_ID = d.DEPT_ID AND e.EMP_STATUS = 'Active'
 GROUP BY d.DEPT_CODE, d.DEPT_NAME, d.DEPT_NAME_AR
 ORDER BY HEADCOUNT DESC`,
    parameters: []
  },
  {
    key: 'updateEmployeePhone',
    name: T('Update Employee Telephone', 'تحديث هاتف موظف'),
    description: T('Corrects the telephone number held against an employee record.',
                   'تصحيح رقم الهاتف المسجّل على بيانات الموظف.'),
    group: 'hr',
    exports: [],
    sql: `UPDATE EMPLOYEES
   SET PHONE = @NewPhone
 WHERE EMP_ID = @EmpId`,
    parameters: [
      P.number('EmpId', T('Employee id', 'رقم الموظف'), 1),
      P.text('NewPhone', T('New telephone number', 'رقم الهاتف الجديد'), 2)
    ]
  }
];

/** Which user groups may run each query group. */
const GROUP_ACCESS = {
  sales: ['salesTeam'],
  service: ['serviceDesk', 'salesTeam'],
  inventory: ['warehouse'],
  finance: ['financeTeam'],
  hr: ['hrTeam'],
  reference: []
};

/** A few individual grants, so the manual can show per-query access too. */
const QUERY_USER_ACCESS = {
  outstandingInvoices: ['n.alqahtani'],
  lowStock: ['k.rahman']
};

const SCHEDULED_TASK = {
  name: T('Nightly Sales Summary Export', 'تصدير ملخص المبيعات اليومي'),
  description: T('Writes the last 30 days of daily sales to an Excel file every morning at 07:00.',
                 'يكتب مبيعات آخر 30 يومًا في ملف Excel كل صباح الساعة 07:00.')
};

// The history the manual's log pages show: who ran what, with which values.
const HISTORY = [
  ['n.alqahtani', 'ordersByPeriod', { FromDate: '2025-06-01', ToDate: '2026-12-31', MinTotal: '5000', OrderStatus: 'ALL' }],
  ['n.alqahtani', 'topCustomers', { FromDate: '2025-06-01', ToDate: '2026-12-31', TopCount: '10' }],
  ['k.rahman', 'salesByRegion', { Regions: '["Central","Eastern"]', FromDate: '2025-06-01', ToDate: '2026-12-31' }],
  ['k.rahman', 'ordersByPeriod', { FromDate: '2026-01-01', ToDate: '2026-12-31', MinTotal: '0', OrderStatus: 'Confirmed' }],
  ['m.zaid', 'customerHistory', { CustomerCode: 'C-0003' }],
  ['m.zaid', 'orderDetails', { OrderNo: 'SO-2026-0200' }],
  ['l.haddad', 'customerDirectory', { NameContains: '', Region: 'Western' }],
  ['l.haddad', 'updateCustomerPhone', { CustomerId: '2', NewPhone: '+966 11 210 0902' }, true],
  ['o.mansour', 'customerHistory', { CustomerCode: 'C-0012' }],
  ['t.ibrahim', 'lowStock', {}],
  ['t.ibrahim', 'catalogue', { Category: 'Electronics', IncludeDiscontinued: 'false' }],
  ['f.said', 'slowMoving', { Months: '3' }],
  ['f.said', 'adjustStock', { ProductId: '2', NewStockQty: '48', NewReorderLevel: '25' }, true],
  ['y.aziz', 'outstandingInvoices', { DueBefore: '2026-12-31', MinAmount: '10000' }],
  ['s.farid', 'invoiceAgeing', { CustomerCode: 'C-0013' }],
  ['h.nasser', 'headcount', {}],
  ['h.nasser', 'employeeDirectory', { Department: '1', Status: 'Active' }],
  ['z.kamal', 'newHires', { FromDate: '2024-01-01', ToDate: '2026-12-31' }],
  ['admin', 'dailySales', {}],
  ['admin', 'ordersByPeriod', { FromDate: '2025-06-01', ToDate: '2026-12-31', MinTotal: '0', OrderStatus: 'ALL' }]
];

// ---------------------------------------------------------------- run

async function reset() {
  const tasks = unwrap(await get('/scheduledtasks'));
  for (const task of tasks) await del(`/scheduledtasks/${task.id}`);
  log(`scheduled tasks removed (${tasks.length})`);

  // Tasks first: a scheduled task item holds a query down (no cascade delete).
  const queries = unwrap(await get('/admin/dynamicqueries'));
  for (const q of queries) await del(`/admin/dynamicqueries/${q.id}`);
  log(`queries removed (${queries.length})`);

  const groups = unwrap(await get('/admin/querygroups'));
  for (const g of groups) await del(`/admin/querygroups/${g.id}`);
  log(`query groups removed (${groups.length})`);

  const userGroups = unwrap(await get('/admin/usergroups'));
  for (const g of userGroups) await del(`/admin/usergroups/${g.id}`);
  log(`user groups removed (${userGroups.length})`);
}

async function ensureUsers(roleIds) {
  const existing = unwrap(await get('/admin/users'));
  const byName = new Map(existing.map(u => [u.username.toLowerCase(), u]));
  const ids = new Map();

  for (const [username, firstName, lastName, email] of DEMO_USERS) {
    const found = byName.get(username.toLowerCase());
    if (found) {
      ids.set(username, found.id);
      continue;
    }
    const created = await post('/admin/users', {
      username, email, firstName, lastName,
      password: DEMO_PASS,
      roleIds: [roleIds.get('User')]
    });
    ids.set(username, created.id);
  }
  log(`demo accounts ready (${ids.size})`);
  return ids;
}

async function main() {
  log(`API ${API}, seeding ${LANG === 'ar' ? 'Arabic' : 'English'} content`);
  token = await loginAs(ADMIN_USER, ADMIN_PASS);

  const roles = unwrap(await get('/admin/roles'));
  const roleIds = new Map(roles.map(r => [r.name, r.id]));

  await reset();

  const userIds = await ensureUsers(roleIds);

  // ---- user groups
  const userGroupIds = new Map();
  for (const g of USER_GROUPS) {
    const created = await post('/admin/usergroups', {
      name: g.name,
      description: g.description,
      memberUserIds: g.members.map(m => userIds.get(m)).filter(Boolean)
    });
    userGroupIds.set(g.key, created.id);
  }
  log(`user groups created (${userGroupIds.size})`);

  // ---- query groups
  const queryGroupIds = new Map();
  for (const g of QUERY_GROUPS) {
    const created = await post('/admin/querygroups', { name: g.name, description: g.description });
    queryGroupIds.set(g.key, created.id);
  }
  log(`query groups created (${queryGroupIds.size})`);

  // ---- queries (lookups first: the drop-downs of the others point at them)
  const lookupIds = new Map();
  const queryIds = new Map();

  const createQuery = async (q) => {
    const parameters = (q.parameters || []).map(p => {
      const { lookup, ...rest } = p;
      return lookup ? { ...rest, dropdownQueryId: lookupIds.get(lookup) } : rest;
    });
    const created = await post('/admin/dynamicqueries', {
      name: q.name,
      description: q.description,
      sqlQuery: q.sql,
      timeoutSeconds: 60,
      isLongRunning: false,
      allowRunWithoutConfirmation: true,
      saveOldValues: true,
      queryGroupId: queryGroupIds.get(q.group),
      allowedExportFormats: q.exports || [],
      parameters
    });
    queryIds.set(q.key, created.id);
    return created.id;
  };

  for (const l of LOOKUPS) {
    const id = await createQuery({ ...l, exports: [], parameters: [] });
    lookupIds.set(l.key, id);
  }
  log(`lookup queries created (${lookupIds.size})`);

  for (const q of QUERIES) await createQuery(q);
  log(`queries created (${QUERIES.length})`);

  // ---- access: Admin can run everything, each team gets its own group
  for (const [groupKey, groupId] of queryGroupIds) {
    await post(`/admin/querygroups/${groupId}/roles`, { roleIds: [roleIds.get('Admin')] });
    const teams = GROUP_ACCESS[groupKey] || [];
    if (teams.length) {
      await post(`/admin/querygroups/${groupId}/user-groups`, {
        userGroupIds: teams.map(t => userGroupIds.get(t)).filter(Boolean)
      });
    }
  }
  for (const [queryKey, usernames] of Object.entries(QUERY_USER_ACCESS)) {
    await post(`/admin/dynamicqueries/${queryIds.get(queryKey)}/users`, {
      userIds: usernames.map(u => userIds.get(u)).filter(Boolean)
    });
  }
  log('access granted');

  // ---- a scheduled task that exports the daily summary every morning
  const task = await post('/scheduledtasks', {
    name: SCHEDULED_TASK.name,
    description: SCHEDULED_TASK.description,
    isEnabled: true,
    outputFolder: TASK_FOLDER,
    combineOutput: false,
    includeHeaders: true,
    timestampFormat: 'yyyy-MM-dd',
    triggers: [{ frequency: 1, timeOfDay: '07:00', sortOrder: 1 }],
    items: [{
      dynamicQueryId: queryIds.get('dailySales'),
      parameters: {},
      exportFormat: 0,
      csvSeparator: ',',
      fileNamePrefix: 'daily-sales',
      appendTimestamp: true,
      sortOrder: 1
    }],
    viewerUserIds: [userIds.get('n.alqahtani'), userIds.get('y.aziz')].filter(Boolean),
    downloadUserIds: [userIds.get('n.alqahtani')].filter(Boolean)
  });
  log(`scheduled task created: ${task.name}`);

  await post(`/scheduledtasks/${task.id}/run`, {});
  log('scheduled task run once, so its run history is not empty');

  // ---- execution history
  let ok = 0;
  const failures = [];
  for (const [username, queryKey, parameters, confirmed] of HISTORY) {
    const id = queryIds.get(queryKey);
    if (!id) { failures.push(`${queryKey}: not created`); continue; }
    const saved = token;
    try {
      token = username === 'admin' ? saved : await loginAs(username, DEMO_PASS);
      await post(`/user/queries/${id}/execute`, { parameters, confirmed: !!confirmed });
      ok++;
    } catch (err) {
      failures.push(`${username} → ${queryKey}: ${err.message}`);
    } finally {
      token = saved;
    }
  }
  log(`execution history: ${ok} run(s) recorded`);
  if (failures.length) {
    log('FAILED runs:');
    for (const f of failures) log('  - ' + f);
  }

  log('done.');
  if (failures.length) process.exitCode = 1;
}

main().catch(err => {
  console.error('[seed] FAILED:', err.message);
  process.exit(1);
});
