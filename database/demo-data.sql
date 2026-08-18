-- ============================================================================
--  Bayan demo dataset - the business tables the user manual is illustrated with
-- ============================================================================
--  Bayan itself stores no business data: it publishes parameterised SQL that runs
--  against whatever schema an organisation already has. The manual therefore needs
--  a plausible one to photograph. This script creates it: a small sales and HR
--  schema (departments, employees, customers, products, orders, order items,
--  invoices) with bilingual English/Arabic names, so the Arabic manual shows
--  Arabic data rather than transliterated English.
--
--  Run it against the same schema the API connects to (ConnectionStrings:
--  DefaultConnection), then run docs/user-manual/seed-demo-data.js to create the
--  queries, groups and users that read it.
--
--      set NLS_LANG=.AL32UTF8
--      sqlplus test/test@host/service @database/demo-data.sql
--
--  Re-runnable: every table is dropped and rebuilt. Dates are anchored on SYSDATE
--  so a manual regenerated next year still shows recent orders.
--
--  NOTE: table and column names avoid the substrings the query validator rejects
--  ("SP_", "XP_", "DBMS_", "UTL_", ";", "--") so that queries written against them
--  pass CreateDynamicQueryValidator.
-- ============================================================================

SET DEFINE OFF
SET SERVEROUTPUT ON

-- ---------------------------------------------------------------- drop (child first)
BEGIN
  FOR t IN (SELECT column_value AS name FROM TABLE(sys.odcivarchar2list(
              'INVOICES', 'ORDER_ITEMS', 'ORDERS', 'PRODUCTS',
              'CUSTOMERS', 'EMPLOYEES', 'DEPARTMENTS'))) LOOP
    BEGIN
      EXECUTE IMMEDIATE 'DROP TABLE ' || t.name || ' CASCADE CONSTRAINTS PURGE';
    EXCEPTION WHEN OTHERS THEN
      IF SQLCODE != -942 THEN RAISE; END IF;   -- -942 = table does not exist
    END;
  END LOOP;
END;
/

-- ---------------------------------------------------------------- departments
CREATE TABLE DEPARTMENTS (
  DEPT_ID      NUMBER(6)      NOT NULL,
  DEPT_CODE    VARCHAR2(10)   NOT NULL,
  DEPT_NAME    NVARCHAR2(80)  NOT NULL,
  DEPT_NAME_AR NVARCHAR2(80)  NOT NULL,
  LOCATION     NVARCHAR2(60)  NOT NULL,
  COST_CENTER  VARCHAR2(12)   NOT NULL,
  CONSTRAINT PK_DEPARTMENTS PRIMARY KEY (DEPT_ID),
  CONSTRAINT UQ_DEPARTMENTS_CODE UNIQUE (DEPT_CODE)
);

INSERT INTO DEPARTMENTS VALUES (1, 'SLS', N'Sales',            N'المبيعات',        N'Head Office',       'CC-1000');
INSERT INTO DEPARTMENTS VALUES (2, 'CSV', N'Customer Service', N'خدمة العملاء',    N'Head Office',       'CC-1100');
INSERT INTO DEPARTMENTS VALUES (3, 'WHS', N'Warehouse',        N'المستودع',        N'Industrial Area',   'CC-2000');
INSERT INTO DEPARTMENTS VALUES (4, 'FIN', N'Finance',          N'المالية',         N'Head Office',       'CC-3000');
INSERT INTO DEPARTMENTS VALUES (5, 'HRD', N'Human Resources',  N'الموارد البشرية', N'Head Office',       'CC-3100');
INSERT INTO DEPARTMENTS VALUES (6, 'ITD', N'Information Technology', N'تقنية المعلومات', N'Head Office',  'CC-4000');
INSERT INTO DEPARTMENTS VALUES (7, 'PRC', N'Procurement',      N'المشتريات',       N'Head Office',       'CC-2100');
INSERT INTO DEPARTMENTS VALUES (8, 'LOG', N'Logistics',        N'الخدمات اللوجستية', N'Port Branch',     'CC-2200');

-- ---------------------------------------------------------------- employees
CREATE TABLE EMPLOYEES (
  EMP_ID         NUMBER(6)      NOT NULL,
  EMP_NO         VARCHAR2(10)   NOT NULL,
  FULL_NAME      NVARCHAR2(120) NOT NULL,
  FULL_NAME_AR   NVARCHAR2(120) NOT NULL,
  DEPT_ID        NUMBER(6)      NOT NULL,
  JOB_TITLE      NVARCHAR2(80)  NOT NULL,
  EMAIL          VARCHAR2(120)  NOT NULL,
  PHONE          VARCHAR2(30)   NOT NULL,
  HIRE_DATE      DATE           NOT NULL,
  MONTHLY_SALARY NUMBER(10,2)   NOT NULL,
  EMP_STATUS     VARCHAR2(12)   NOT NULL,
  CONSTRAINT PK_EMPLOYEES PRIMARY KEY (EMP_ID),
  CONSTRAINT UQ_EMPLOYEES_NO UNIQUE (EMP_NO),
  CONSTRAINT FK_EMPLOYEES_DEPT FOREIGN KEY (DEPT_ID) REFERENCES DEPARTMENTS (DEPT_ID)
);

INSERT INTO EMPLOYEES VALUES (1,  'E-1001', N'Nora Al-Qahtani', N'نورة القحطاني', 1, N'Sales Manager',         'nora.q@example.com',   '+966 11 400 1001', SYSDATE-2540, 21500, 'Active');
INSERT INTO EMPLOYEES VALUES (2,  'E-1002', N'Khalid Rahman',   N'خالد رحمن',     1, N'Senior Sales Rep',      'khalid.r@example.com', '+966 11 400 1002', SYSDATE-1830, 14200, 'Active');
INSERT INTO EMPLOYEES VALUES (3,  'E-1003', N'Maha Zaid',       N'مها زايد',      1, N'Sales Rep',             'maha.z@example.com',   '+966 11 400 1003', SYSDATE-960,  11800, 'Active');
INSERT INTO EMPLOYEES VALUES (4,  'E-1004', N'Faris Al-Otaibi', N'فارس العتيبي',  1, N'Sales Rep',             'faris.o@example.com',  '+966 11 400 1004', SYSDATE-420,  10900, 'Active');
INSERT INTO EMPLOYEES VALUES (5,  'E-1005', N'Layla Haddad',    N'ليلى حداد',     2, N'Service Desk Lead',     'layla.h@example.com',  '+966 11 400 1005', SYSDATE-2130, 15600, 'Active');
INSERT INTO EMPLOYEES VALUES (6,  'E-1006', N'Omar Mansour',    N'عمر منصور',     2, N'Service Agent',         'omar.m@example.com',   '+966 11 400 1006', SYSDATE-880,  9800,  'Active');
INSERT INTO EMPLOYEES VALUES (7,  'E-1007', N'Reem Sultan',     N'ريم سلطان',     2, N'Service Agent',         'reem.s@example.com',   '+966 11 400 1007', SYSDATE-610,  9400,  'On Leave');
INSERT INTO EMPLOYEES VALUES (8,  'E-1008', N'Tarek Ibrahim',   N'طارق إبراهيم',  3, N'Warehouse Supervisor',  'tarek.i@example.com',  '+966 13 500 1008', SYSDATE-2960, 13700, 'Active');
INSERT INTO EMPLOYEES VALUES (9,  'E-1009', N'Fatima Said',     N'فاطمة سعيد',    3, N'Inventory Controller',  'fatima.s@example.com', '+966 13 500 1009', SYSDATE-1450, 11200, 'Active');
INSERT INTO EMPLOYEES VALUES (10, 'E-1010', N'Bandar Hussein',  N'بندر حسين',     3, N'Store Keeper',          'bandar.h@example.com', '+966 13 500 1010', SYSDATE-700,  8600,  'Active');
INSERT INTO EMPLOYEES VALUES (11, 'E-1011', N'Yousef Aziz',     N'يوسف عزيز',     4, N'Finance Manager',       'yousef.a@example.com', '+966 11 400 1011', SYSDATE-3300, 23800, 'Active');
INSERT INTO EMPLOYEES VALUES (12, 'E-1012', N'Salma Farid',     N'سلمى فريد',     4, N'Accounts Receivable',   'salma.f@example.com',  '+966 11 400 1012', SYSDATE-1240, 12900, 'Active');
INSERT INTO EMPLOYEES VALUES (13, 'E-1013', N'Ahmad Nabil',     N'أحمد نبيل',     4, N'Accountant',            'ahmad.n@example.com',  '+966 11 400 1013', SYSDATE-540,  11400, 'Active');
INSERT INTO EMPLOYEES VALUES (14, 'E-1014', N'Huda Nasser',     N'هدى ناصر',      5, N'HR Manager',            'huda.n@example.com',   '+966 11 400 1014', SYSDATE-2780, 20400, 'Active');
INSERT INTO EMPLOYEES VALUES (15, 'E-1015', N'Ziad Kamal',      N'زياد كمال',     5, N'HR Officer',            'ziad.k@example.com',   '+966 11 400 1015', SYSDATE-980,  11700, 'Active');
INSERT INTO EMPLOYEES VALUES (16, 'E-1016', N'Amal Yassin',     N'أمل ياسين',     5, N'Payroll Officer',       'amal.y@example.com',   '+966 11 400 1016', SYSDATE-310,  10800, 'Active');
INSERT INTO EMPLOYEES VALUES (17, 'E-1017', N'Rami Sabbagh',    N'رامي صباغ',     6, N'Systems Analyst',       'rami.s@example.com',   '+966 11 400 1017', SYSDATE-1690, 16300, 'Active');
INSERT INTO EMPLOYEES VALUES (18, 'E-1018', N'Dina Halabi',     N'دينا حلبي',     6, N'Database Administrator','dina.h@example.com',   '+966 11 400 1018', SYSDATE-1120, 17500, 'Active');
INSERT INTO EMPLOYEES VALUES (19, 'E-1019', N'Sami Barakat',    N'سامي بركات',    7, N'Procurement Officer',   'sami.b@example.com',   '+966 11 400 1019', SYSDATE-1560, 12600, 'Active');
INSERT INTO EMPLOYEES VALUES (20, 'E-1020', N'Noura Fahad',     N'نورة فهد',      7, N'Buyer',                 'noura.f@example.com',  '+966 11 400 1020', SYSDATE-480,  10300, 'Active');
INSERT INTO EMPLOYEES VALUES (21, 'E-1021', N'Majed Sharif',    N'ماجد شريف',     8, N'Logistics Coordinator', 'majed.s@example.com',  '+966 13 500 1021', SYSDATE-2210, 13100, 'Active');
INSERT INTO EMPLOYEES VALUES (22, 'E-1022', N'Hanan Rashid',    N'حنان راشد',     8, N'Dispatch Clerk',        'hanan.r@example.com',  '+966 13 500 1022', SYSDATE-390,  9100,  'Active');
INSERT INTO EMPLOYEES VALUES (23, 'E-1023', N'Waleed Amin',     N'وليد أمين',     1, N'Sales Analyst',         'waleed.a@example.com', '+966 11 400 1023', SYSDATE-1310, 12200, 'Resigned');
INSERT INTO EMPLOYEES VALUES (24, 'E-1024', N'Sara Mustafa',    N'سارة مصطفى',    2, N'Service Agent',         'sara.m@example.com',   '+966 11 400 1024', SYSDATE-150,  8900,  'Active');

-- ---------------------------------------------------------------- customers
CREATE TABLE CUSTOMERS (
  CUSTOMER_ID      NUMBER(6)      NOT NULL,
  CUSTOMER_CODE    VARCHAR2(12)   NOT NULL,
  CUSTOMER_NAME    NVARCHAR2(120) NOT NULL,
  CUSTOMER_NAME_AR NVARCHAR2(120) NOT NULL,
  REGION           NVARCHAR2(40)  NOT NULL,
  CITY             NVARCHAR2(40)  NOT NULL,
  SEGMENT          VARCHAR2(20)   NOT NULL,
  CONTACT_NAME     NVARCHAR2(80)  NOT NULL,
  CONTACT_EMAIL    VARCHAR2(120)  NOT NULL,
  CONTACT_PHONE    VARCHAR2(30)   NOT NULL,
  CREDIT_LIMIT     NUMBER(12,2)   NOT NULL,
  IS_ACTIVE        NUMBER(1)      DEFAULT 1 NOT NULL,
  CREATED_ON       DATE           NOT NULL,
  CONSTRAINT PK_CUSTOMERS PRIMARY KEY (CUSTOMER_ID),
  CONSTRAINT UQ_CUSTOMERS_CODE UNIQUE (CUSTOMER_CODE)
);

INSERT INTO CUSTOMERS VALUES (1,  'C-0001', N'Al Waha Trading',       N'الواحة للتجارة',           N'Central',  N'Riyadh',   'Wholesale',  N'Fahad Al-Nasser', 'fahad@alwaha.example.com',        '+966 11 210 0001', 500000,  1, SYSDATE-1420);
INSERT INTO CUSTOMERS VALUES (2,  'C-0002', N'Bright Home Stores',    N'بيت الإضاءة',              N'Central',  N'Riyadh',   'Retail',     N'Mona Sulaiman',   'mona@brighthome.example.com',     '+966 11 210 0002', 120000,  1, SYSDATE-1310);
INSERT INTO CUSTOMERS VALUES (3,  'C-0003', N'Gulf Contracting Co',   N'الخليج للمقاولات',         N'Eastern',  N'Dammam',   'Corporate',  N'Saeed Al-Amri',   'saeed@gulfcc.example.com',        '+966 13 220 0003', 900000,  1, SYSDATE-1290);
INSERT INTO CUSTOMERS VALUES (4,  'C-0004', N'Red Sea Hotels',        N'فنادق البحر الأحمر',       N'Western',  N'Jeddah',   'Corporate',  N'Lina Barakat',    'lina@redseahotels.example.com',   '+966 12 230 0004', 750000,  1, SYSDATE-1180);
INSERT INTO CUSTOMERS VALUES (5,  'C-0005', N'Najd Supplies',         N'نجد للتوريدات',            N'Central',  N'Buraydah', 'Wholesale',  N'Turki Al-Harbi',  'turki@najd.example.com',          '+966 16 240 0005', 300000,  1, SYSDATE-1120);
INSERT INTO CUSTOMERS VALUES (6,  'C-0006', N'Coastal Markets',       N'أسواق الساحل',             N'Western',  N'Yanbu',    'Retail',     N'Rania Fouad',     'rania@coastal.example.com',       '+966 14 250 0006', 90000,   1, SYSDATE-1050);
INSERT INTO CUSTOMERS VALUES (7,  'C-0007', N'Ministry of Education', N'وزارة التعليم',            N'Central',  N'Riyadh',   'Government', N'Abdullah Saad',   'procurement@moe.example.com',     '+966 11 260 0007', 2000000, 1, SYSDATE-1010);
INSERT INTO CUSTOMERS VALUES (8,  'C-0008', N'Falcon Logistics',      N'صقر للخدمات اللوجستية',    N'Eastern',  N'Jubail',   'Corporate',  N'Hassan Idris',    'hassan@falconlog.example.com',    '+966 13 270 0008', 450000,  1, SYSDATE-980);
INSERT INTO CUSTOMERS VALUES (9,  'C-0009', N'Green Valley Farms',    N'مزارع الوادي الأخضر',      N'Southern', N'Abha',     'Wholesale',  N'Khalil Zahrani',  'khalil@greenvalley.example.com',  '+966 17 280 0009', 220000,  1, SYSDATE-930);
INSERT INTO CUSTOMERS VALUES (10, 'C-0010', N'Desert Rose Retail',    N'وردة الصحراء',             N'Northern', N'Tabuk',    'Retail',     N'Amani Yusuf',     'amani@desertrose.example.com',    '+966 14 290 0010', 75000,   1, SYSDATE-880);
INSERT INTO CUSTOMERS VALUES (11, 'C-0011', N'Pearl Interiors',       N'اللؤلؤة للديكور',          N'Eastern',  N'Khobar',   'Corporate',  N'Basem Qadi',      'basem@pearl.example.com',         '+966 13 300 0011', 380000,  1, SYSDATE-840);
INSERT INTO CUSTOMERS VALUES (12, 'C-0012', N'Sunrise Pharmacies',    N'صيدليات الشروق',           N'Central',  N'Riyadh',   'Retail',     N'Dalia Munir',     'dalia@sunrise.example.com',       '+966 11 310 0012', 150000,  1, SYSDATE-790);
INSERT INTO CUSTOMERS VALUES (13, 'C-0013', N'Atlas Steel Works',     N'أطلس للصناعات الحديدية',   N'Eastern',  N'Dammam',   'Corporate',  N'Nabil Sarhan',    'nabil@atlassteel.example.com',    '+966 13 320 0013', 1100000, 1, SYSDATE-760);
INSERT INTO CUSTOMERS VALUES (14, 'C-0014', N'Oasis Catering',        N'الواحة للتموين',           N'Western',  N'Jeddah',   'Wholesale',  N'Iman Shafiq',     'iman@oasiscater.example.com',     '+966 12 330 0014', 260000,  1, SYSDATE-720);
INSERT INTO CUSTOMERS VALUES (15, 'C-0015', N'City Municipality',     N'أمانة المدينة',            N'Western',  N'Madinah',  'Government', N'Osama Rifai',     'tenders@city.example.com',        '+966 14 340 0015', 1600000, 1, SYSDATE-690);
INSERT INTO CUSTOMERS VALUES (16, 'C-0016', N'Blue Nile Traders',     N'تجار النيل الأزرق',        N'Central',  N'Riyadh',   'Wholesale',  N'Gamal Mahdi',     'gamal@bluenile.example.com',      '+966 11 350 0016', 340000,  1, SYSDATE-640);
INSERT INTO CUSTOMERS VALUES (17, 'C-0017', N'Northern Lights Co',    N'أضواء الشمال',             N'Northern', N'Arar',     'Retail',     N'Wafa Jarrah',     'wafa@northlights.example.com',    '+966 14 360 0017', 60000,   1, SYSDATE-600);
INSERT INTO CUSTOMERS VALUES (18, 'C-0018', N'Sahara Motors',         N'الصحراء للسيارات',         N'Central',  N'Riyadh',   'Corporate',  N'Ibrahim Kanaan',  'ibrahim@saharamotors.example.com','+966 11 370 0018', 820000,  1, SYSDATE-560);
INSERT INTO CUSTOMERS VALUES (19, 'C-0019', N'Palm Grove Hotels',     N'فنادق واحة النخيل',        N'Southern', N'Najran',   'Corporate',  N'Suha Adel',       'suha@palmgrove.example.com',      '+966 17 380 0019', 410000,  1, SYSDATE-520);
INSERT INTO CUSTOMERS VALUES (20, 'C-0020', N'Metro Electronics',     N'مترو للإلكترونيات',        N'Eastern',  N'Khobar',   'Retail',     N'Tamer Wahba',     'tamer@metroelec.example.com',     '+966 13 390 0020', 180000,  1, SYSDATE-480);
INSERT INTO CUSTOMERS VALUES (21, 'C-0021', N'Al Fanar Services',     N'الفنار للخدمات',           N'Western',  N'Jeddah',   'Corporate',  N'Randa Salim',     'randa@alfanar.example.com',       '+966 12 400 0021', 520000,  1, SYSDATE-440);
INSERT INTO CUSTOMERS VALUES (22, 'C-0022', N'Highland Bakeries',     N'مخابز المرتفعات',          N'Southern', N'Abha',     'Retail',     N'Faisal Ghamdi',   'faisal@highland.example.com',     '+966 17 410 0022', 95000,   1, SYSDATE-400);
INSERT INTO CUSTOMERS VALUES (23, 'C-0023', N'Unified Health Group',  N'المجموعة الصحية الموحدة',  N'Central',  N'Riyadh',   'Corporate',  N'Maysoon Attar',   'maysoon@uhg.example.com',         '+966 11 420 0023', 1300000, 1, SYSDATE-360);
INSERT INTO CUSTOMERS VALUES (24, 'C-0024', N'Seaside Resorts',       N'منتجعات الشاطئ',           N'Western',  N'Jeddah',   'Corporate',  N'Karim Douaji',    'karim@seaside.example.com',       '+966 12 430 0024', 640000,  0, SYSDATE-330);
INSERT INTO CUSTOMERS VALUES (25, 'C-0025', N'Orbit Office Supply',   N'أوربت للقرطاسية',          N'Central',  N'Riyadh',   'Wholesale',  N'Nadia Kurdi',     'nadia@orbit.example.com',         '+966 11 440 0025', 130000,  1, SYSDATE-300);
INSERT INTO CUSTOMERS VALUES (26, 'C-0026', N'Eastern Petro Services',N'الشرقية للخدمات البترولية',N'Eastern',  N'Dhahran',  'Corporate',  N'Marwan Talal',    'marwan@eps.example.com',          '+966 13 450 0026', 1750000, 1, SYSDATE-260);
INSERT INTO CUSTOMERS VALUES (27, 'C-0027', N'Silk Road Textiles',    N'طريق الحرير للنسيج',       N'Western',  N'Makkah',   'Wholesale',  N'Hala Mardini',    'hala@silkroad.example.com',       '+966 12 460 0027', 240000,  1, SYSDATE-210);
INSERT INTO CUSTOMERS VALUES (28, 'C-0028', N'Royal Gardens Co',      N'الحدائق الملكية',          N'Central',  N'Riyadh',   'Retail',     N'Anas Bakri',      'anas@royalgardens.example.com',   '+966 11 470 0028', 110000,  1, SYSDATE-170);
INSERT INTO CUSTOMERS VALUES (29, 'C-0029', N'Northern Cement',       N'أسمنت الشمال',             N'Northern', N'Hail',     'Corporate',  N'Sultan Rawi',     'sultan@ncement.example.com',      '+966 16 480 0029', 980000,  1, SYSDATE-120);
INSERT INTO CUSTOMERS VALUES (30, 'C-0030', N'Bay View Cafes',        N'مقاهي الخليج',             N'Eastern',  N'Khobar',   'Retail',     N'Rasha Nouri',     'rasha@bayview.example.com',       '+966 13 490 0030', 70000,   0, SYSDATE-80);

-- ---------------------------------------------------------------- products
CREATE TABLE PRODUCTS (
  PRODUCT_ID      NUMBER(6)      NOT NULL,
  SKU             VARCHAR2(20)   NOT NULL,
  PRODUCT_NAME    NVARCHAR2(100) NOT NULL,
  PRODUCT_NAME_AR NVARCHAR2(100) NOT NULL,
  CATEGORY        NVARCHAR2(40)  NOT NULL,
  UNIT_PRICE      NUMBER(10,2)   NOT NULL,
  STOCK_QTY       NUMBER(8)      NOT NULL,
  REORDER_LEVEL   NUMBER(8)      NOT NULL,
  IS_ACTIVE       NUMBER(1)      DEFAULT 1 NOT NULL,
  CONSTRAINT PK_PRODUCTS PRIMARY KEY (PRODUCT_ID),
  CONSTRAINT UQ_PRODUCTS_SKU UNIQUE (SKU)
);

INSERT INTO PRODUCTS VALUES (1,  'SKU-OFF-001', N'Executive Desk 180cm',    N'مكتب تنفيذي 180سم',      N'Office Furniture', 2450.00, 42,  20,  1);
INSERT INTO PRODUCTS VALUES (2,  'SKU-OFF-002', N'Ergonomic Chair',         N'كرسي مريح',              N'Office Furniture', 890.00,  18,  25,  1);
INSERT INTO PRODUCTS VALUES (3,  'SKU-OFF-003', N'Meeting Table 12 Seats',  N'طاولة اجتماعات 12 مقعد', N'Office Furniture', 5200.00, 7,   5,   1);
INSERT INTO PRODUCTS VALUES (4,  'SKU-OFF-004', N'Filing Cabinet 4 Drawer', N'خزانة ملفات 4 أدراج',    N'Office Furniture', 1150.00, 33,  15,  1);
INSERT INTO PRODUCTS VALUES (5,  'SKU-ELC-001', N'LED Monitor 27 inch',     N'شاشة LED 27 بوصة',       N'Electronics',      1290.00, 64,  30,  1);
INSERT INTO PRODUCTS VALUES (6,  'SKU-ELC-002', N'Laser Printer A4',        N'طابعة ليزر A4',          N'Electronics',      2100.00, 12,  15,  1);
INSERT INTO PRODUCTS VALUES (7,  'SKU-ELC-003', N'Network Switch 24 Port',  N'موزع شبكة 24 منفذ',      N'Electronics',      3400.00, 9,   10,  1);
INSERT INTO PRODUCTS VALUES (8,  'SKU-ELC-004', N'Wireless Access Point',   N'نقطة وصول لاسلكية',      N'Electronics',      760.00,  55,  20,  1);
INSERT INTO PRODUCTS VALUES (9,  'SKU-ELC-005', N'Barcode Scanner',         N'قارئ باركود',            N'Electronics',      540.00,  27,  15,  1);
INSERT INTO PRODUCTS VALUES (10, 'SKU-STA-001', N'A4 Paper Box 5 Reams',    N'صندوق ورق A4 - 5 رزم',   N'Stationery',       98.00,   310, 100, 1);
INSERT INTO PRODUCTS VALUES (11, 'SKU-STA-002', N'Ink Cartridge Black',     N'خرطوشة حبر أسود',        N'Stationery',       210.00,  88,  60,  1);
INSERT INTO PRODUCTS VALUES (12, 'SKU-STA-003', N'Whiteboard 150cm',        N'سبورة بيضاء 150سم',      N'Stationery',       340.00,  24,  12,  1);
INSERT INTO PRODUCTS VALUES (13, 'SKU-STA-004', N'Document Binder Pack',    N'حزمة مجلدات مستندات',    N'Stationery',       65.00,   402, 150, 1);
INSERT INTO PRODUCTS VALUES (14, 'SKU-CLN-001', N'Industrial Cleaner 5L',   N'منظف صناعي 5 لتر',       N'Cleaning',         120.00,  140, 80,  1);
INSERT INTO PRODUCTS VALUES (15, 'SKU-CLN-002', N'Disinfectant Wipes',      N'مناديل معقمة',           N'Cleaning',         45.00,   68,  90,  1);
INSERT INTO PRODUCTS VALUES (16, 'SKU-CLN-003', N'Floor Polisher',          N'ماكينة تلميع أرضيات',    N'Cleaning',         4300.00, 4,   3,   1);
INSERT INTO PRODUCTS VALUES (17, 'SKU-SAF-001', N'Safety Helmet',           N'خوذة سلامة',             N'Safety',           130.00,  210, 100, 1);
INSERT INTO PRODUCTS VALUES (18, 'SKU-SAF-002', N'High Visibility Vest',    N'سترة عاكسة',             N'Safety',           85.00,   175, 120, 1);
INSERT INTO PRODUCTS VALUES (19, 'SKU-SAF-003', N'Fire Extinguisher 6kg',   N'طفاية حريق 6كجم',        N'Safety',           390.00,  46,  40,  1);
INSERT INTO PRODUCTS VALUES (20, 'SKU-SAF-004', N'First Aid Kit',           N'حقيبة إسعافات أولية',    N'Safety',           250.00,  31,  35,  1);
INSERT INTO PRODUCTS VALUES (21, 'SKU-KIT-001', N'Water Dispenser',         N'موزع مياه',              N'Pantry',           980.00,  22,  10,  1);
INSERT INTO PRODUCTS VALUES (22, 'SKU-KIT-002', N'Coffee Machine',          N'ماكينة قهوة',            N'Pantry',           1750.00, 11,  8,   1);
INSERT INTO PRODUCTS VALUES (23, 'SKU-KIT-003', N'Paper Cups Carton',       N'كرتون أكواب ورقية',      N'Pantry',           130.00,  95,  70,  1);
INSERT INTO PRODUCTS VALUES (24, 'SKU-OFF-005', N'Reception Sofa 3 Seats',  N'كنبة استقبال 3 مقاعد',   N'Office Furniture', 3600.00, 6,   4,   0);

-- ---------------------------------------------------------------- orders / lines / invoices
CREATE TABLE ORDERS (
  ORDER_ID       NUMBER(8)     NOT NULL,
  ORDER_NO       VARCHAR2(16)  NOT NULL,
  CUSTOMER_ID    NUMBER(6)     NOT NULL,
  SALES_REP_ID   NUMBER(6)     NOT NULL,
  ORDER_DATE     DATE          NOT NULL,
  DELIVERY_DATE  DATE,
  ORDER_STATUS   VARCHAR2(16)  NOT NULL,
  PAYMENT_METHOD VARCHAR2(20)  NOT NULL,
  REGION         NVARCHAR2(40) NOT NULL,
  TOTAL_AMOUNT   NUMBER(12,2)  DEFAULT 0 NOT NULL,
  CURRENCY       VARCHAR2(3)   DEFAULT 'SAR' NOT NULL,
  CONSTRAINT PK_ORDERS PRIMARY KEY (ORDER_ID),
  CONSTRAINT UQ_ORDERS_NO UNIQUE (ORDER_NO),
  CONSTRAINT FK_ORDERS_CUSTOMER FOREIGN KEY (CUSTOMER_ID) REFERENCES CUSTOMERS (CUSTOMER_ID),
  CONSTRAINT FK_ORDERS_REP FOREIGN KEY (SALES_REP_ID) REFERENCES EMPLOYEES (EMP_ID)
);

CREATE TABLE ORDER_ITEMS (
  ITEM_ID    NUMBER(10)   NOT NULL,
  ORDER_ID   NUMBER(8)    NOT NULL,
  PRODUCT_ID NUMBER(6)    NOT NULL,
  QUANTITY   NUMBER(6)    NOT NULL,
  UNIT_PRICE NUMBER(10,2) NOT NULL,
  LINE_TOTAL NUMBER(12,2) NOT NULL,
  CONSTRAINT PK_ORDER_ITEMS PRIMARY KEY (ITEM_ID),
  CONSTRAINT FK_ITEMS_ORDER FOREIGN KEY (ORDER_ID) REFERENCES ORDERS (ORDER_ID),
  CONSTRAINT FK_ITEMS_PRODUCT FOREIGN KEY (PRODUCT_ID) REFERENCES PRODUCTS (PRODUCT_ID)
);

CREATE TABLE INVOICES (
  INVOICE_ID     NUMBER(8)    NOT NULL,
  INVOICE_NO     VARCHAR2(16) NOT NULL,
  ORDER_ID       NUMBER(8)    NOT NULL,
  CUSTOMER_ID    NUMBER(6)    NOT NULL,
  ISSUE_DATE     DATE         NOT NULL,
  DUE_DATE       DATE         NOT NULL,
  AMOUNT         NUMBER(12,2) NOT NULL,
  PAID_AMOUNT    NUMBER(12,2) DEFAULT 0 NOT NULL,
  INVOICE_STATUS VARCHAR2(16) NOT NULL,
  CONSTRAINT PK_INVOICES PRIMARY KEY (INVOICE_ID),
  CONSTRAINT UQ_INVOICES_NO UNIQUE (INVOICE_NO),
  CONSTRAINT FK_INVOICES_ORDER FOREIGN KEY (ORDER_ID) REFERENCES ORDERS (ORDER_ID),
  CONSTRAINT FK_INVOICES_CUSTOMER FOREIGN KEY (CUSTOMER_ID) REFERENCES CUSTOMERS (CUSTOMER_ID)
);

-- Orders, their lines and their invoices are generated rather than listed: 240 orders
-- over the last ~15 months. The arithmetic is deterministic (MOD, not random), so two
-- runs of this script produce the same dataset.
DECLARE
  v_item_id  NUMBER := 0;
  v_inv_id   NUMBER := 0;
  v_cust     NUMBER;
  v_rep      NUMBER;
  v_status   VARCHAR2(16);
  v_pay      VARCHAR2(20);
  v_region   NVARCHAR2(40);
  v_date     DATE;
  v_lines    NUMBER;
  v_prod     NUMBER;
  v_qty      NUMBER;
  v_price    NUMBER;
  v_total    NUMBER;
  v_inv_stat VARCHAR2(16);
  v_paid     NUMBER;
  TYPE t_reps IS VARRAY(4) OF NUMBER;
  v_reps     t_reps := t_reps(1, 2, 3, 4);
BEGIN
  FOR i IN 1 .. 240 LOOP
    v_cust := MOD(i * 7, 30) + 1;
    v_rep  := v_reps(MOD(i, 4) + 1);
    v_date := TRUNC(SYSDATE) - 450 + TRUNC(i * 15 / 8);

    -- Recent orders are still moving through the pipeline, older ones are settled.
    IF    MOD(i, 17) = 0            THEN v_status := 'Cancelled';
    ELSIF v_date > SYSDATE - 14     THEN v_status := 'New';
    ELSIF v_date > SYSDATE - 45     THEN v_status := 'Confirmed';
    ELSIF v_date > SYSDATE - 90     THEN v_status := 'Shipped';
    ELSE                                 v_status := 'Delivered';
    END IF;

    v_pay := CASE MOD(i, 4)
               WHEN 0 THEN 'Bank Transfer'
               WHEN 1 THEN 'Credit'
               WHEN 2 THEN 'Card'
               ELSE        'Cash'
             END;

    SELECT REGION INTO v_region FROM CUSTOMERS WHERE CUSTOMER_ID = v_cust;

    INSERT INTO ORDERS (ORDER_ID, ORDER_NO, CUSTOMER_ID, SALES_REP_ID, ORDER_DATE,
                        DELIVERY_DATE, ORDER_STATUS, PAYMENT_METHOD, REGION, TOTAL_AMOUNT, CURRENCY)
    VALUES (i, 'SO-' || TO_CHAR(EXTRACT(YEAR FROM v_date)) || '-' || LPAD(i, 4, '0'),
            v_cust, v_rep, v_date,
            CASE WHEN v_status IN ('Shipped', 'Delivered') THEN v_date + MOD(i, 9) + 2 END,
            v_status, v_pay, v_region, 0, 'SAR');

    v_lines := MOD(i, 4) + 1;   -- 1 to 4 lines per order
    v_total := 0;
    FOR l IN 1 .. v_lines LOOP
      v_prod := MOD((i * 5) + (l * 11), 24) + 1;
      v_qty  := MOD((i * 3) + l, 12) + 1;
      SELECT UNIT_PRICE INTO v_price FROM PRODUCTS WHERE PRODUCT_ID = v_prod;
      v_item_id := v_item_id + 1;
      INSERT INTO ORDER_ITEMS (ITEM_ID, ORDER_ID, PRODUCT_ID, QUANTITY, UNIT_PRICE, LINE_TOTAL)
      VALUES (v_item_id, i, v_prod, v_qty, v_price, v_qty * v_price);
      v_total := v_total + (v_qty * v_price);
    END LOOP;

    UPDATE ORDERS SET TOTAL_AMOUNT = v_total WHERE ORDER_ID = i;

    -- Cancelled and brand-new orders are not invoiced yet.
    IF v_status NOT IN ('Cancelled', 'New') THEN
      v_inv_id := v_inv_id + 1;
      IF v_status = 'Delivered' AND MOD(i, 5) <> 0 THEN
        v_inv_stat := 'Paid';    v_paid := v_total;
      ELSIF v_date + 30 < SYSDATE THEN
        v_inv_stat := 'Overdue'; v_paid := ROUND(v_total * MOD(i, 3) / 10, 2);
      ELSE
        v_inv_stat := 'Issued';  v_paid := 0;
      END IF;

      INSERT INTO INVOICES (INVOICE_ID, INVOICE_NO, ORDER_ID, CUSTOMER_ID, ISSUE_DATE,
                            DUE_DATE, AMOUNT, PAID_AMOUNT, INVOICE_STATUS)
      VALUES (v_inv_id, 'INV-' || LPAD(v_inv_id, 5, '0'), i, v_cust,
              v_date + 1, v_date + 31, v_total, v_paid, v_inv_stat);
    END IF;
  END LOOP;

  COMMIT;
  DBMS_OUTPUT.PUT_LINE('Generated 240 orders, ' || v_item_id || ' order lines, ' || v_inv_id || ' invoices.');
END;
/

CREATE INDEX IX_ORDERS_DATE     ON ORDERS (ORDER_DATE);
CREATE INDEX IX_ORDERS_CUSTOMER ON ORDERS (CUSTOMER_ID);
CREATE INDEX IX_ORDERS_STATUS   ON ORDERS (ORDER_STATUS);
CREATE INDEX IX_ITEMS_ORDER     ON ORDER_ITEMS (ORDER_ID);
CREATE INDEX IX_INVOICES_STATUS ON INVOICES (INVOICE_STATUS);
CREATE INDEX IX_EMPLOYEES_DEPT  ON EMPLOYEES (DEPT_ID);

COMMIT;

-- ---------------------------------------------------------------- summary
SET LINES 120
SELECT 'DEPARTMENTS' AS TABLE_NAME, COUNT(*) AS ROW_COUNT FROM DEPARTMENTS
UNION ALL SELECT 'EMPLOYEES',   COUNT(*) FROM EMPLOYEES
UNION ALL SELECT 'CUSTOMERS',   COUNT(*) FROM CUSTOMERS
UNION ALL SELECT 'PRODUCTS',    COUNT(*) FROM PRODUCTS
UNION ALL SELECT 'ORDERS',      COUNT(*) FROM ORDERS
UNION ALL SELECT 'ORDER_ITEMS', COUNT(*) FROM ORDER_ITEMS
UNION ALL SELECT 'INVOICES',    COUNT(*) FROM INVOICES;

EXIT
