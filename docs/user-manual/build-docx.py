#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Builds the DotNetDBTasks user manual as a Word document, in English and Arabic.

    python build-docx.py            # both languages
    python build-docx.py en         # one language

Reads screenshots/<lang>/*.png (produced by capture-screenshots.js) and writes
DotNetDBTasks-User-Manual-EN.docx / -AR.docx. A screenshot that is missing is replaced by a
visible placeholder rather than failing the build, so a manual is always produced.

The text lives in `build_manual()` near the bottom. Every string is written once as
T("English", "العربية") and the active language picks one, so the two documents cannot drift
apart — add a section and you are forced to supply both.
"""

import os
import struct
import sys
from datetime import date

try:
    from docx import Document
    from docx.enum.table import WD_TABLE_ALIGNMENT
    from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_BREAK
    from docx.oxml import OxmlElement
    from docx.oxml.ns import qn
    from docx.shared import Emu, Inches, Pt, RGBColor
except ImportError:
    sys.exit("python-docx is required:  python -m pip install python-docx")

HERE = os.path.dirname(os.path.abspath(__file__))

MAX_IMG_W = Inches(6.6)
MAX_IMG_H = Inches(7.6)

ACCENT = RGBColor(0x37, 0x30, 0xA3)
MUTED = RGBColor(0x5B, 0x61, 0x6E)
HEADER_FILL = "3730A3"
NOTE_FILL = "EEF1FB"
WARN_FILL = "FDF0E6"

# Latin body font; Arabic runs get a complex-script font that ships with Windows and Office.
LATIN_FONT = "Calibri"
ARABIC_FONT = "Segoe UI"

LANG = "en"
_figure_no = [0]


def T(en, ar):
    """The active language's version of a string."""
    return en if LANG == "en" else ar


def is_rtl():
    return LANG == "ar"


# --------------------------------------------------------------------------- xml helpers

def _shade(cell, fill):
    shd = OxmlElement("w:shd")
    shd.set(qn("w:val"), "clear")
    shd.set(qn("w:fill"), fill)
    cell._tc.get_or_add_tcPr().append(shd)


def _field(paragraph, instr):
    """Inserts a Word field (used for the TOC and page numbers)."""
    run = paragraph.add_run()
    begin = OxmlElement("w:fldChar")
    begin.set(qn("w:fldCharType"), "begin")
    itxt = OxmlElement("w:instrText")
    itxt.set(qn("xml:space"), "preserve")
    itxt.text = instr
    sep = OxmlElement("w:fldChar")
    sep.set(qn("w:fldCharType"), "separate")
    end = OxmlElement("w:fldChar")
    end.set(qn("w:fldCharType"), "end")
    for el in (begin, itxt, sep, end):
        run._r.append(el)


def _png_size(path):
    with open(path, "rb") as fh:
        head = fh.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    return struct.unpack(">II", head[16:24])


def _mark_rtl_run(run):
    """Tells Word the run is complex-script text, and which font to shape it with."""
    rpr = run._r.get_or_add_rPr()
    rtl = OxmlElement("w:rtl")
    rtl.set(qn("w:val"), "1")
    rpr.append(rtl)
    rfonts = rpr.find(qn("w:rFonts"))
    if rfonts is None:
        rfonts = OxmlElement("w:rFonts")
        rpr.insert(0, rfonts)
    rfonts.set(qn("w:cs"), ARABIC_FONT)
    rfonts.set(qn("w:ascii"), ARABIC_FONT)
    rfonts.set(qn("w:hAnsi"), ARABIC_FONT)


def _mark_rtl_paragraph(paragraph):
    ppr = paragraph._p.get_or_add_pPr()
    bidi = OxmlElement("w:bidi")
    bidi.set(qn("w:val"), "1")
    ppr.append(bidi)
    for run in paragraph.runs:
        _mark_rtl_run(run)


def apply_rtl(doc):
    """Flips the finished document to right-to-left.

    Doing it as a final pass rather than inside every helper keeps the content code free of
    language plumbing — and guarantees nothing is missed, which per-call flags would not.
    """
    for section in doc.sections:
        sect_pr = section._sectPr
        bidi = OxmlElement("w:bidi")
        sect_pr.append(bidi)

    def walk_paragraphs(container):
        for paragraph in container.paragraphs:
            _mark_rtl_paragraph(paragraph)
        for table in container.tables:
            # bidiVisual mirrors the column order, so the first column sits on the right.
            tbl_pr = table._tbl.tblPr
            visual = OxmlElement("w:bidiVisual")
            tbl_pr.append(visual)
            for row in table.rows:
                for cell in row.cells:
                    walk_paragraphs(cell)

    walk_paragraphs(doc)
    for section in doc.sections:
        for part in (section.header, section.footer):
            for paragraph in part.paragraphs:
                _mark_rtl_paragraph(paragraph)


# --------------------------------------------------------------------------- doc helpers

def new_document():
    doc = Document()

    section = doc.sections[0]
    section.page_width = Inches(8.27)
    section.page_height = Inches(11.69)
    section.top_margin = section.bottom_margin = Inches(0.85)
    section.left_margin = section.right_margin = Inches(0.85)

    body_font = ARABIC_FONT if is_rtl() else LATIN_FONT
    normal = doc.styles["Normal"]
    normal.font.name = body_font
    normal.font.size = Pt(11 if is_rtl() else 10.5)
    normal.paragraph_format.space_after = Pt(7)
    # Arabic needs more leading than Latin at the same size.
    normal.paragraph_format.line_spacing = 1.45 if is_rtl() else 1.12

    for name, size, color, before in (
        ("Heading 1", 20, ACCENT, 22),
        ("Heading 2", 15, ACCENT, 16),
        ("Heading 3", 12.5, RGBColor(0x1F, 0x25, 0x37), 12),
        ("Heading 4", 11, RGBColor(0x1F, 0x25, 0x37), 10),
    ):
        st = doc.styles[name]
        st.font.name = body_font
        st.font.size = Pt(size)
        st.font.color.rgb = color
        st.font.bold = True
        st.paragraph_format.space_before = Pt(before)
        st.paragraph_format.space_after = Pt(6)
        st.paragraph_format.keep_with_next = True

    return doc


def add_page_footer(doc):
    footer = doc.sections[0].footer
    p = footer.paragraphs[0]
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.add_run(T("DotNetDBTasks User Manual  ·  page ",
                "دليل مستخدم DotNetDBTasks  ·  صفحة "))
    _field(p, "PAGE")
    for r in p.runs:
        r.font.size = Pt(8.5)
        r.font.color.rgb = MUTED


def h1(doc, text):
    doc.add_heading(text, level=1)


def h2(doc, text):
    doc.add_heading(text, level=2)


def h3(doc, text):
    doc.add_heading(text, level=3)


def para(doc, text, bold=False, italic=False, size=None):
    p = doc.add_paragraph()
    run = p.add_run(text)
    run.bold = bold
    run.italic = italic
    if size:
        run.font.size = Pt(size)
    return p


def bullets(doc, items, style="List Bullet"):
    for item in items:
        if isinstance(item, tuple):
            lead, rest = item
            p = doc.add_paragraph(style=style)
            p.add_run(lead).bold = True
            p.add_run(rest)
        else:
            doc.add_paragraph(item, style=style)


def numbered(doc, items):
    bullets(doc, items, style="List Number")


def note(doc, text, kind="note"):
    """A shaded callout box."""
    label = {
        "note": T("Note.  ", "ملاحظة.  "),
        "warning": T("Important.  ", "تنبيه.  "),
    }[kind]
    fill = WARN_FILL if kind == "warning" else NOTE_FILL
    table = doc.add_table(rows=1, cols=1)
    table.alignment = WD_TABLE_ALIGNMENT.CENTER
    cell = table.cell(0, 0)
    _shade(cell, fill)
    p = cell.paragraphs[0]
    p.paragraph_format.space_before = Pt(4)
    p.paragraph_format.space_after = Pt(4)
    lead = p.add_run(label)
    lead.bold = True
    lead.font.color.rgb = ACCENT if kind != "warning" else RGBColor(0xB4, 0x53, 0x09)
    lead.font.size = Pt(10)
    body = p.add_run(text)
    body.font.size = Pt(10)
    doc.add_paragraph()


def table(doc, header, rows, widths=None, font_size=9):
    t = doc.add_table(rows=1, cols=len(header))
    t.style = "Table Grid"
    t.alignment = WD_TABLE_ALIGNMENT.CENTER

    # Long tables (the permission matrix especially) split across pages; without this the
    # continuation arrives as unlabelled columns.
    tr_pr = t.rows[0]._tr.get_or_add_trPr()
    repeat = OxmlElement("w:tblHeader")
    repeat.set(qn("w:val"), "true")
    tr_pr.append(repeat)

    for i, text in enumerate(header):
        cell = t.rows[0].cells[i]
        _shade(cell, HEADER_FILL)
        p = cell.paragraphs[0]
        p.paragraph_format.space_after = Pt(2)
        run = p.add_run(text)
        run.bold = True
        run.font.size = Pt(font_size)
        run.font.color.rgb = RGBColor(0xFF, 0xFF, 0xFF)
    for r_i, row in enumerate(rows):
        cells = t.add_row().cells
        for i, text in enumerate(row):
            if r_i % 2 == 1:
                _shade(cells[i], "F4F5FA")
            p = cells[i].paragraphs[0]
            p.paragraph_format.space_after = Pt(2)
            run = p.add_run(str(text))
            run.font.size = Pt(font_size)
            if i == 0 and len(header) > 2:
                run.bold = True
    if widths:
        for row in t.rows:
            for i, w in enumerate(widths):
                row.cells[i].width = Inches(w)
    doc.add_paragraph()
    return t


def figure(doc, name, caption):
    """Inserts screenshots/<lang>/<name>.png scaled to fit, with a numbered caption."""
    _figure_no[0] += 1
    number = _figure_no[0]
    path = os.path.join(HERE, "screenshots", LANG, name + ".png")

    if not os.path.exists(path):
        t = doc.add_table(rows=1, cols=1)
        cell = t.cell(0, 0)
        _shade(cell, WARN_FILL)
        run = cell.paragraphs[0].add_run(T(
            f"[ Screenshot '{LANG}/{name}.png' was not captured. Re-run 'npm run capture' "
            f"with the application running to include it. ]",
            f"[ لم يتم التقاط الصورة '{LANG}/{name}.png'. أعد تشغيل 'npm run capture' "
            f"والتطبيق قيد التشغيل لتضمينها. ]"))
        run.italic = True
        run.font.size = Pt(9)
        doc.add_paragraph()
    else:
        size = _png_size(path)
        width = MAX_IMG_W
        if size:
            px_w, px_h = size
            if Emu(int(MAX_IMG_W * px_h / px_w)) > MAX_IMG_H:
                width = Emu(int(MAX_IMG_H * px_w / px_h))
        p = doc.add_paragraph()
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        p.paragraph_format.space_after = Pt(3)
        p.add_run().add_picture(path, width=width)

    cap = doc.add_paragraph()
    cap.alignment = WD_ALIGN_PARAGRAPH.CENTER
    cap.paragraph_format.space_after = Pt(12)
    run = cap.add_run(T(f"Figure {number}. {caption}", f"شكل {number}. {caption}"))
    run.italic = True
    run.font.size = Pt(9)
    run.font.color.rgb = MUTED


def page_break(doc):
    doc.add_paragraph().add_run().add_break(WD_BREAK.PAGE)


# --------------------------------------------------------------------------- front matter

def cover(doc):
    for _ in range(4):
        doc.add_paragraph()

    def centered(text, size, color, bold=False, italic=False):
        p = doc.add_paragraph()
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        run = p.add_run(text)
        run.font.size = Pt(size)
        run.font.color.rgb = color
        run.bold = bold
        run.italic = italic

    centered("DotNetDBTasks", 40, ACCENT, bold=True)
    centered(T("User Manual", "دليل المستخدم"), 24, RGBColor(0x1F, 0x25, 0x37))
    centered(T("Features, screens and permissions",
               "الميزات والشاشات والصلاحيات"), 13, MUTED, italic=True)

    for _ in range(10):
        doc.add_paragraph()

    centered(T("A dynamic database query platform: authored SQL, controlled access,\n"
               "audited execution, and scheduled exports.",
               "منصّة استعلامات ديناميكية لقواعد البيانات: استعلامات SQL مُعدّة مسبقًا،\n"
               "ووصول محكوم، وتنفيذ مُدقَّق، وتصدير مجدول."), 11, MUTED)

    for _ in range(6):
        doc.add_paragraph()

    centered(T(f"Generated {date.today().strftime('%d %B %Y')}",
               f"تاريخ الإصدار {date.today().strftime('%Y-%m-%d')}"), 9.5, MUTED)
    page_break(doc)


def toc(doc):
    h1(doc, T("Contents", "المحتويات"))
    p = doc.add_paragraph()
    _field(p, r'TOC \o "1-3" \h \z \u')
    para(doc, T("Right-click the table above and choose “Update Field” to populate or refresh "
                "it (Word builds the entry list when the document is opened).",
                "انقر بزر الفأرة الأيمن على الجدول أعلاه واختر «تحديث الحقل» لتعبئته أو "
                "تحديثه (يبني Word قائمة العناوين عند فتح المستند)."),
         italic=True, size=9)
    page_break(doc)


# --------------------------------------------------------------------------- the manual

def build_manual(doc):

    # ===================================================================== 1
    h1(doc, T("1. About this manual", "١. عن هذا الدليل"))
    para(doc, T(
        "This manual describes every screen in DotNetDBTasks, what each control does, and "
        "exactly which role is allowed to use it. It is written for four audiences, and each "
        "chapter says at the top which of them it is for:",
        "يصف هذا الدليل كل شاشة في نظام DotNetDBTasks، ووظيفة كل عنصر فيها، والدور المسموح له "
        "باستخدامها بالتحديد. وهو موجَّه إلى أربع فئات، ويوضّح كل فصل في بدايته الفئة التي "
        "يخاطبها:"))
    bullets(doc, [
        (T("Users — ", "المستخدمون — "),
         T("people who run queries that have been assigned to them and read their own results.",
           "من ينفّذون الاستعلامات المخصَّصة لهم ويطّلعون على نتائجهم.")),
        (T("Administrators — ", "المسؤولون — "),
         T("people who author queries, organise them, grant access, schedule exports and "
           "manage accounts.",
           "من يكتبون الاستعلامات وينظّمونها ويمنحون الصلاحيات ويجدولون التصدير ويديرون "
           "الحسابات.")),
        (T("Auditors — ", "المدققون — "),
         T("people who review what was run and what was changed, without being able to run or "
           "change anything themselves.",
           "من يراجعون ما جرى تنفيذه وما جرى تغييره، دون أن يتمكنوا من التنفيذ أو التعديل.")),
        (T("Access Managers — ", "مديرو الصلاحيات — "),
         T("people who decide who may reach which queries, without being able to see the SQL "
           "or run it.",
           "من يحدّدون مَن يصل إلى أي استعلام، دون أن يروا نص SQL أو ينفّذوه.")),
    ])
    para(doc, T(
        "Chapters 2 to 4 apply to everyone. Chapter 5 is the day-to-day guide for Users. "
        "Chapters 6 to 11 are administration. Chapter 12 covers the two audit trails.",
        "الفصول من ٢ إلى ٤ تخصّ الجميع. والفصل ٥ هو الدليل اليومي للمستخدمين. والفصول من ٦ "
        "إلى ١١ تخصّ الإدارة. ويتناول الفصل ١٢ سجلَّي التدقيق."))
    note(doc, T(
        "Every screenshot in this manual was taken from a live installation, in the dark "
        "theme. If a screen in front of you differs, your account may hold a different set of "
        "roles — Chapter 4 explains what each role can see.",
        "التُقطت جميع الصور في هذا الدليل من نظام حقيقي قيد التشغيل، بالوضع الداكن. وإذا "
        "اختلفت الشاشة أمامك، فقد يحمل حسابك مجموعة أدوار مختلفة — ويوضّح الفصل ٤ ما يراه كل "
        "دور."))

    # ===================================================================== 2
    h1(doc, T("2. What the system does", "٢. ما الذي يقوم به النظام"))
    para(doc, T(
        "DotNetDBTasks lets a small number of people write SQL queries once, and lets a much "
        "larger number of people run those queries safely, without database access and without "
        "being able to change the SQL. Around that idea sit access control, a full audit "
        "trail, and a scheduler that runs the same queries unattended and writes the results "
        "to files.",
        "يتيح نظام DotNetDBTasks لعدد محدود من الأشخاص كتابة استعلامات SQL مرة واحدة، ثم يتيح "
        "لعدد أكبر بكثير تنفيذ تلك الاستعلامات بأمان، دون أن يملكوا وصولًا إلى قاعدة البيانات "
        "ودون قدرة على تعديل نص SQL. وتحيط بهذه الفكرة إدارةٌ للصلاحيات، وسجلُّ تدقيق كامل، "
        "ومجدولٌ ينفّذ الاستعلامات نفسها دون تدخل ويكتب النتائج في ملفات."))

    h2(doc, T("2.1 The core idea", "٢.١ الفكرة الأساسية"))
    numbered(doc, [
        T("An administrator writes a query and declares its parameters — for example a start "
          "date, an end date and a department.",
          "يكتب المسؤول استعلامًا ويعرّف معاملاته — مثل تاريخ بداية وتاريخ نهاية وقسم."),
        T("The administrator decides who may run it: by role, by department, by named user, or "
          "by putting it in a query group and granting the group.",
          "يحدّد المسؤول مَن يستطيع تنفيذه: بحسب الدور، أو القسم، أو مستخدم بعينه، أو بوضعه في "
          "مجموعة استعلامات ومنح المجموعة."),
        T("A user opens the query, fills in a generated form, and runs it. They never see or "
          "edit the SQL.",
          "يفتح المستخدم الاستعلام ويعبّئ نموذجًا يُبنى تلقائيًا ثم ينفّذه. ولا يرى نص SQL ولا "
          "يعدّله إطلاقًا."),
        T("The result is paged, sorted and filtered on the server and can be exported to "
          "Excel, CSV, PDF, Word or JSON.",
          "تُقسَّم النتيجة إلى صفحات وتُرتَّب وتُصفّى على الخادم، ويمكن تصديرها إلى Excel أو "
          "CSV أو PDF أو Word أو JSON."),
        T("Every run is written to the execution log; every administrative change is written "
          "to the system audit trail.",
          "يُسجَّل كل تنفيذ في سجل التنفيذ، ويُسجَّل كل تغيير إداري في سجل عمليات النظام."),
    ])

    h2(doc, T("2.2 Key terms", "٢.٢ المصطلحات الأساسية"))
    table(doc,
          [T("Term", "المصطلح"), T("Meaning", "المعنى")],
          [
              [T("Dynamic query", "استعلام ديناميكي"),
               T("A stored, named SQL statement with declared parameters. It may read (SELECT) "
                 "or write (INSERT / UPDATE / DELETE).",
                 "جملة SQL محفوظة ولها اسم ومعاملات معرَّفة. وقد تكون للقراءة (SELECT) أو "
                 "للتعديل (INSERT / UPDATE / DELETE).")],
              [T("Parameter", "معامل"),
               T("A declared input on a query. Five types: String, Number, Date, Boolean and "
                 "Dropdown.",
                 "مُدخَل معرَّف في الاستعلام. وأنواعه خمسة: نص، ورقم، وتاريخ، وقيمة منطقية، "
                 "وقائمة منسدلة.")],
              [T("Query group", "مجموعة استعلامات"),
               T("A folder of queries. Granting access to the group grants all the queries "
                 "inside it.",
                 "مجلد يضم استعلامات. ومنح الوصول إلى المجموعة يمنح كل الاستعلامات داخلها.")],
              [T("Database connection", "اتصال قاعدة بيانات"),
               T("A named set of database credentials (“DB User”) that a query runs through. "
                 "Passwords are encrypted at rest.",
                 "مجموعة بيانات اعتماد لقاعدة بيانات لها اسم («مستخدم قاعدة البيانات») يُنفَّذ "
                 "الاستعلام من خلالها. وكلمات المرور مشفَّرة عند التخزين.")],
              [T("Execution", "تنفيذ"),
               T("One run of one query by one person, recorded in the execution log.",
                 "تشغيل واحد لاستعلام واحد من شخص واحد، يُسجَّل في سجل التنفيذ.")],
              [T("Scheduled task", "مهمة مجدولة"),
               T("One or more queries run unattended on a timetable, with the results written "
                 "to files on the server.",
                 "استعلام أو أكثر يُنفَّذ تلقائيًا وفق جدول زمني، وتُكتب النتائج في ملفات على "
                 "الخادم.")],
              [T("Role", "دور"),
               T("A named permission set held by an account. An account may hold more than "
                 "one; permissions add up.",
                 "مجموعة صلاحيات لها اسم يحملها الحساب. وقد يحمل الحساب أكثر من دور، "
                 "وتُجمَع الصلاحيات.")],
          ],
          widths=[1.6, 5.0])

    h2(doc, T("2.3 How a query is kept safe", "٢.٣ كيف يُحفظ أمان الاستعلام"))
    bullets(doc, [
        (T("Values are never pasted into SQL. ", "لا تُدمج القيم في نص SQL إطلاقًا. "),
         T("Every parameter is bound through the database driver, so a value cannot change the "
           "meaning of the statement.",
           "يُمرَّر كل معامل عبر مشغّل قاعدة البيانات، فلا تستطيع أي قيمة تغيير معنى الجملة.")),
        (T("Write queries are previewed first. ", "تُعاين استعلامات التعديل أولًا. "),
         T("An INSERT, UPDATE or DELETE runs inside a transaction that is rolled back, so you "
           "see how many rows would change — and for UPDATE and DELETE, which rows — before "
           "anything is committed.",
           "تُنفَّذ جملة INSERT أو UPDATE أو DELETE داخل معاملة يجري التراجع عنها، فترى عدد "
           "الصفوف التي ستتغيّر — والصفوف نفسها في حالتَي UPDATE وDELETE — قبل اعتماد أي "
           "تغيير.")),
        (T("Before-change values are captured. ", "تُحفظ القيم قبل التغيير. "),
         T("For UPDATE and DELETE, the rows as they were before the change are stored with the "
           "execution log.",
           "في حالتَي UPDATE وDELETE تُخزَّن الصفوف بحالتها السابقة مع سجل التنفيذ.")),
        (T("Timeouts are enforced. ", "تُطبَّق مهل التنفيذ. "),
         T("Each query carries its own timeout; slow queries can be marked long-running so "
           "they are not cut off by a network gateway.",
           "لكل استعلام مهلته الخاصة، ويمكن وسم الاستعلامات البطيئة بأنها طويلة التنفيذ حتى لا "
           "تقطعها بوابة الشبكة.")),
        (T("Credentials are encrypted. ", "بيانات الاعتماد مشفَّرة. "),
         T("Database passwords are stored with AES-256 encryption and are never included in an "
           "export file.",
           "تُخزَّن كلمات مرور قواعد البيانات بتشفير AES-256 ولا تُدرَج مطلقًا في ملف "
           "تصدير.")),
    ])

    page_break(doc)

    # ===================================================================== 3
    h1(doc, T("3. Getting started", "٣. البدء"))

    h2(doc, T("3.1 Signing in", "٣.١ تسجيل الدخول"))
    para(doc, T(
        "Open the application address in a browser and sign in with your username and "
        "password. Accounts are either local to the application or imported from Active "
        "Directory — in both cases you sign in on the same form.",
        "افتح عنوان التطبيق في المتصفح وسجّل الدخول باسم المستخدم وكلمة المرور. والحسابات إما "
        "محلية داخل التطبيق أو مستوردة من Active Directory — وفي الحالتين يجري تسجيل الدخول من "
        "النموذج نفسه."))
    figure(doc, "01-login",
           T("The sign-in page. The two buttons in the top corner switch the language and the "
             "colour theme before you sign in.",
             "صفحة تسجيل الدخول. يتيح الزران في الزاوية العليا تبديل اللغة ونمط الألوان قبل "
             "تسجيل الدخول."))
    bullets(doc, [
        (T("Show / hide password — ", "إظهار/إخفاء كلمة المرور — "),
         T("the eye icon inside the password box.", "أيقونة العين داخل حقل كلمة المرور.")),
        (T("Language — ", "اللغة — "),
         T("the translate icon in the top corner switches between English and Arabic. It is "
           "available before sign-in so that the form itself can be read in either language.",
           "أيقونة الترجمة في الزاوية العليا تبدّل بين العربية والإنجليزية، وهي متاحة قبل "
           "تسجيل الدخول حتى يمكن قراءة النموذج نفسه بأي من اللغتين.")),
        (T("Theme — ", "النمط — "),
         T("the sun/moon icon switches between the light and dark themes.",
           "أيقونة الشمس/القمر تبدّل بين الوضعين الفاتح والداكن.")),
    ])
    para(doc, T(
        "After signing in you land on the first page your roles allow: administrators and "
        "Access Managers on Manage Queries, Auditors on the execution logs, and everyone else "
        "on My Queries.",
        "بعد تسجيل الدخول تصل إلى أول صفحة تسمح بها أدوارك: المسؤولون ومديرو الصلاحيات إلى "
        "«إدارة الاستعلامات»، والمدققون إلى «سجلات التنفيذ»، وسائر المستخدمين إلى "
        "«استعلاماتي»."))

    h2(doc, T("3.2 The main window", "٣.٢ الواجهة الرئيسية"))
    para(doc, T(
        "The bar across the top is the whole navigation. On one side is the site logo (or the "
        "application name when no logo has been uploaded); on the other are the destinations "
        "your roles can reach, then the account menu.",
        "الشريط العلوي هو كامل نظام التنقل. على أحد طرفيه شعار الموقع (أو اسم التطبيق إن لم "
        "يُرفع شعار)، وعلى الطرف الآخر الوجهات التي تسمح بها أدوارك، ثم قائمة الحساب."))
    figure(doc, "02-admin-landing",
           T("The application as an administrator sees it: My Queries, History, and the "
             "Queries, Users & Access and Audit menus, with the account menu at the end.",
             "التطبيق كما يراه المسؤول: «استعلاماتي» و«السجل» وقوائم «الاستعلامات» "
             "و«المستخدمون والصلاحيات» و«التدقيق»، وقائمة الحساب في الطرف."))
    para(doc, T("The navigation is grouped into three drop-down menus plus the always-visible "
                "personal pages:",
                "يُقسَّم التنقل إلى ثلاث قوائم منسدلة، إضافةً إلى الصفحات الشخصية الظاهرة "
                "دائمًا:"))
    table(doc,
          [T("Menu", "القائمة"), T("Contains", "تحتوي على"), T("Visible to", "تظهر لـ")],
          [
              [T("My Queries", "استعلاماتي"),
               T("The queries assigned to you", "الاستعلامات المخصَّصة لك"),
               T("Everyone", "الجميع")],
              [T("History", "السجل"),
               T("Your own execution history", "سجل تنفيذك الشخصي"),
               T("Everyone", "الجميع")],
              [T("Schedules", "المهام المجدولة"),
               T("Status of scheduled tasks you were named on",
                 "حالة المهام المجدولة التي سُمّيت ضمن مشاهديها"),
               T("Users (hidden for Admin and Auditor, who use the full Scheduled Tasks page)",
                 "المستخدمون (تُخفى عن المسؤول والمدقق لأنهما يستخدمان صفحة المهام الكاملة)")],
              [T("Queries", "الاستعلامات"),
               T("Manage Queries, Query Groups, Schedules",
                 "إدارة الاستعلامات، ومجموعات الاستعلامات، والمهام المجدولة"),
               T("Admin, Access Manager, Auditor (each sees only its own entries)",
                 "المسؤول ومدير الصلاحيات والمدقق (يرى كلٌّ منهم عناصره فقط)")],
              [T("Users & Access", "المستخدمون والصلاحيات"),
               T("Users, AD Users, DB Users",
                 "المستخدمون، ومستخدمو Active Directory، ومستخدمو قواعد البيانات"),
               T("Admin, Access Manager", "المسؤول ومدير الصلاحيات")],
              [T("Audit", "التدقيق"),
               T("Logs, System Audit", "سجلات التنفيذ، وسجل عمليات النظام"),
               T("Admin, Auditor", "المسؤول والمدقق")],
              [T("Account", "الحساب"),
               T("Language, theme, Settings, Website logo, Logout",
                 "اللغة، والنمط، والإعدادات، وشعار الموقع، وتسجيل الخروج"),
               T("Everyone", "الجميع")],
          ],
          widths=[1.4, 2.9, 2.3])
    figure(doc, "04-nav-queries",
           T("The Queries menu. An Access Manager sees only the first two entries; an Auditor "
             "sees only Schedules.",
             "قائمة «الاستعلامات». يرى مدير الصلاحيات أول عنصرين فقط، ويرى المدقق «المهام "
             "المجدولة» وحدها."))
    figure(doc, "05-nav-people",
           T("The Users & Access menu. AD Users and DB Users are administrator-only; an Access "
             "Manager sees only Users.",
             "قائمة «المستخدمون والصلاحيات». «مستخدمو Active Directory» و«مستخدمو قواعد "
             "البيانات» للمسؤول وحده، ولا يرى مدير الصلاحيات سوى «المستخدمون»."))
    figure(doc, "06-nav-audit",
           T("The Audit menu — the two read-only trails, for Admin and Auditor.",
             "قائمة «التدقيق» — السجلّان للقراءة فقط، وهما للمسؤول والمدقق."))

    h2(doc, T("3.3 The account menu", "٣.٣ قائمة الحساب"))
    para(doc, T(
        "The circular icon at the end of the bar opens the account menu. It shows who you are "
        "signed in as and holds the personal settings.",
        "تفتح الأيقونة الدائرية في طرف الشريط قائمة الحساب، وهي تعرض هوية المستخدم الحالي "
        "وتضم الإعدادات الشخصية."))
    figure(doc, "03-account-menu",
           T("The account menu as an administrator sees it. Settings and Website logo appear "
             "only for administrators.",
             "قائمة الحساب كما يراها المسؤول. لا يظهر عنصرا «الإعدادات» و«شعار الموقع» إلا "
             "للمسؤولين."))
    bullets(doc, [
        (T("Switch to العربية / English — ", "التبديل إلى English / العربية — "),
         T("changes the interface language. The page reloads.",
           "يغيّر لغة الواجهة، وتُعاد تحميل الصفحة.")),
        (T("Switch to dark / light mode — ", "التبديل إلى الوضع الداكن/الفاتح — "),
         T("changes the colour theme immediately; the menu stays open so you can compare.",
           "يغيّر نمط الألوان فورًا، وتبقى القائمة مفتوحة للمقارنة.")),
        (T("Settings — ", "الإعدادات — "),
         T("system-wide toggles (administrators only, see Chapter 14).",
           "مفاتيح على مستوى النظام (للمسؤولين فقط، انظر الفصل ١٤).")),
        (T("Website logo — ", "شعار الموقع — "),
         T("upload or remove the logo in the top bar (administrators only, see Chapter 13).",
           "رفع شعار الشريط العلوي أو إزالته (للمسؤولين فقط، انظر الفصل ١٣).")),
        (T("Logout — ", "تسجيل الخروج — "),
         T("signs out and returns to the sign-in page.",
           "ينهي الجلسة ويعيدك إلى صفحة تسجيل الدخول.")),
    ])

    h2(doc, T("3.4 Language and reading direction", "٣.٤ اللغة واتجاه القراءة"))
    para(doc, T(
        "The interface ships in English and Arabic. Switching language flips the whole layout "
        "between left-to-right and right-to-left, including menus, tables and dialogs. Your "
        "choice is remembered on that browser.",
        "تتوفر الواجهة بالعربية والإنجليزية. وتبديل اللغة يقلب التخطيط بالكامل بين اليسار "
        "واليمين، بما في ذلك القوائم والجداول والنوافذ. ويُحفظ اختيارك في هذا المتصفح."))
    figure(doc, "81-alternate-language",
           T("The same page in Arabic. The layout mirrors: navigation, table columns and the "
             "account menu all move to the opposite side.",
             "الصفحة نفسها بالإنجليزية. ينعكس التخطيط: التنقل وأعمدة الجداول وقائمة الحساب "
             "تنتقل جميعها إلى الجهة المقابلة."))
    note(doc, T(
        "Data is shown in its own direction regardless of the interface language — an Arabic "
        "query name reads correctly in the English interface and vice versa. SQL, file paths "
        "and connection strings always stay left-to-right.",
        "تُعرض البيانات باتجاهها الأصلي بغض النظر عن لغة الواجهة — فاسم الاستعلام العربي يُقرأ "
        "صحيحًا في الواجهة الإنجليزية والعكس. أما نص SQL ومسارات الملفات وسلاسل الاتصال فتبقى "
        "دائمًا من اليسار إلى اليمين."))

    h2(doc, T("3.5 Light and dark themes", "٣.٥ الوضعان الفاتح والداكن"))
    para(doc, T(
        "This manual is illustrated in the dark theme. The light theme carries exactly the "
        "same pages and controls.",
        "صُوِّر هذا الدليل بالوضع الداكن. ويحتوي الوضع الفاتح على الصفحات والعناصر نفسها "
        "تمامًا."))
    figure(doc, "80-alternate-theme",
           T("The light theme. Every page, dialog and message supports both; the choice is per "
             "browser, not per account.",
             "الوضع الفاتح. تدعم كل الصفحات والنوافذ والرسائل الوضعين، والاختيار مرتبط "
             "بالمتصفح لا بالحساب."))

    page_break(doc)

    # ===================================================================== 4
    h1(doc, T("4. Roles and permissions", "٤. الأدوار والصلاحيات"))
    para(doc, T(
        "This chapter is the authoritative reference for who can do what. Permissions are "
        "enforced by the server on every request — the interface hides what you cannot use, "
        "but hiding is a convenience, not the control.",
        "هذا الفصل هو المرجع المعتمد لتحديد صلاحيات كل دور. وتُطبَّق الصلاحيات على الخادم في "
        "كل طلب — أما إخفاء الواجهة لما لا تملكه فهو تسهيل، وليس هو وسيلة الضبط."))

    h2(doc, T("4.1 The four roles", "٤.١ الأدوار الأربعة"))
    para(doc, T(
        "Roles are records in the system, not fixed categories, and one account may hold "
        "several. When an account holds more than one role, its permissions are the sum of "
        "them — with one deliberate exception described in section 4.3.",
        "الأدوار سجلات في النظام وليست فئات ثابتة، وقد يحمل الحساب الواحد أكثر من دور. وعندما "
        "يحمل الحساب أكثر من دور تكون صلاحياته مجموع صلاحياتها — مع استثناء واحد مقصود يوضّحه "
        "البند ٤.٣."))
    table(doc,
          [T("Role", "الدور"), T("Purpose", "الغرض")],
          [
              ["Admin",
               T("Full control: authors queries, grants access, schedules tasks, manages "
                 "accounts and connections, and reads both audit trails.",
                 "تحكم كامل: يكتب الاستعلامات، ويمنح الصلاحيات، ويجدول المهام، ويدير الحسابات "
                 "والاتصالات، ويطّلع على سجلَّي التدقيق.")],
              ["User",
               T("Runs the queries assigned to them and reads their own history. The everyday "
                 "role.",
                 "ينفّذ الاستعلامات المخصَّصة له ويطّلع على سجله الشخصي. وهو الدور اليومي.")],
              ["Auditor",
               T("Reads the execution logs, the before-change snapshots, the system audit "
                 "trail and the scheduled-task history. Cannot run a query and cannot change "
                 "anything.",
                 "يطّلع على سجلات التنفيذ، وصور الصفوف قبل التغيير، وسجل عمليات النظام، وسجل "
                 "تشغيل المهام المجدولة. ولا يستطيع تنفيذ استعلام ولا تعديل أي شيء.")],
              ["AccessManager",
               T("Decides who may reach which queries and manages user accounts. Cannot see a "
                 "query's SQL, cannot edit a query, and cannot run one.",
                 "يحدّد مَن يصل إلى أي استعلام ويدير حسابات المستخدمين. ولا يرى نص SQL، ولا "
                 "يعدّل استعلامًا، ولا ينفّذه.")],
          ],
          widths=[1.5, 5.1])

    h2(doc, T("4.2 Permission matrix", "٤.٢ جدول الصلاحيات"))
    para(doc, T("“Yes” means the role has the permission outright. Anything else is a "
                "condition.",
                "«نعم» تعني أن الدور يملك الصلاحية كاملة. وما عدا ذلك فهو مشروط."))
    yes, dash, never = T("Yes", "نعم"), "—", T("Never", "أبدًا")
    assigned = T("Assigned only", "المخصَّص له فقط")
    table(doc,
          [T("Capability", "الصلاحية"), "Admin", "Auditor", "AccessManager", "User"],
          [
              [T("Create / edit / delete queries", "إنشاء الاستعلامات وتعديلها وحذفها"),
               yes, dash, dash, dash],
              [T("Read a query's SQL", "الاطلاع على نص SQL للاستعلام"),
               yes, dash, never, assigned],
              [T("Run a query", "تنفيذ استعلام"), yes, never, never, assigned],
              [T("Export / import query definitions", "تصدير تعريفات الاستعلامات واستيرادها"),
               yes, dash, dash, dash],
              [T("Create / edit / delete query groups", "إنشاء مجموعات الاستعلامات وتعديلها وحذفها"),
               yes, dash, dash, dash],
              [T("Assign a query group to roles / departments / users",
                 "إسناد مجموعة استعلامات إلى أدوار أو أقسام أو مستخدمين"),
               yes, dash, yes, dash],
              [T("Assign a single query to roles / departments / users",
                 "إسناد استعلام مفرد إلى أدوار أو أقسام أو مستخدمين"),
               yes, dash, T("Only if enabled in Settings", "إذا فُعِّل في الإعدادات"), dash],
              [T("List queries and groups (names, no SQL)",
                 "عرض قوائم الاستعلامات والمجموعات (الأسماء دون SQL)"),
               yes, dash, yes, assigned],
              [T("Create and manage user accounts", "إنشاء حسابات المستخدمين وإدارتها"),
               yes, dash, T("Yes, except administrators", "نعم، عدا المسؤولين"), dash],
              [T("Import users from Active Directory", "استيراد المستخدمين من Active Directory"),
               yes, dash, dash, dash],
              [T("Manage database connections", "إدارة اتصالات قواعد البيانات"),
               yes, dash, dash, dash],
              [T("Read execution logs and before-change values",
                 "الاطلاع على سجلات التنفيذ وقيم ما قبل التغيير"),
               yes, yes, dash, T("Own history only", "سجله الشخصي فقط")],
              [T("Read the system audit trail", "الاطلاع على سجل عمليات النظام"),
               yes, yes, dash, dash],
              [T("View scheduled tasks and their run history",
                 "عرض المهام المجدولة وسجل تشغيلها"),
               yes, yes, dash, T("If named as a viewer", "إذا سُمّي مشاهدًا")],
              [T("Download scheduled-task output files", "تنزيل ملفات مخرجات المهام المجدولة"),
               yes, T("No", "لا"), dash, T("If named as a downloader", "إذا سُمّي مُنزِّلًا")],
              [T("Create / edit / run / cancel scheduled tasks",
                 "إنشاء المهام المجدولة وتعديلها وتشغيلها وإلغاؤها"),
               yes, dash, dash, dash],
              [T("Change system settings and the site logo", "تغيير إعدادات النظام وشعار الموقع"),
               yes, dash, dash, dash],
          ],
          widths=[2.9, 0.75, 0.8, 1.3, 1.0],
          font_size=8.5)

    h2(doc, T("4.3 Four rules worth stating outright", "٤.٣ أربع قواعد يجدر ذكرها صراحة"))
    h3(doc, T("Neither oversight role can ever run a query",
              "لا يستطيع أيٌّ من دورَي الرقابة تنفيذ استعلام مطلقًا"))
    para(doc, T(
        "Auditor and Access Manager appear in the access pickers like any other role, but a "
        "query assigned to either of them stays unrunnable: the server drops those two roles "
        "when it works out who may run what. The assignment is inert rather than refused.",
        "يظهر دورا Auditor وAccessManager في قوائم اختيار الصلاحيات كأي دور آخر، لكن الاستعلام "
        "المسنَد إلى أيٍّ منهما يبقى غير قابل للتنفيذ: إذ يستبعد الخادم هذين الدورين عند تحديد "
        "من يحق له التنفيذ. فالإسناد يبقى بلا أثر بدل أن يُرفض."))
    para(doc, T(
        "This applies to the role, not the person. Someone who holds both Auditor and User "
        "runs whatever the User role has been assigned — the Auditor part simply adds no query "
        "access.",
        "وهذا يسري على الدور لا على الشخص. فمن يحمل دورَي Auditor وUser ينفّذ ما أُسنِد إلى "
        "دور User — ولا يضيف دور Auditor أي صلاحية تنفيذ."))

    h3(doc, T("An Access Manager manages group access, not per-query access, by default",
              "يدير مدير الصلاحيات وصول المجموعات لا الاستعلامات المفردة، افتراضيًا"))
    para(doc, T(
        "Groups are the coarser and safer control: granting a group is a deliberate, visible "
        "act, where per-query grants accumulate quietly. An administrator can switch per-query "
        "access on for the role under Settings (Chapter 14). While it is off, an attempt to "
        "change per-query access is refused and the refusal is recorded in the system audit "
        "trail.",
        "المجموعات أداة ضبط أوسع وأكثر أمانًا: فمنح مجموعة إجراء مقصود وظاهر، بينما تتراكم "
        "الأذونات المفردة بصمت. ويستطيع المسؤول تفعيل صلاحية الاستعلام المفرد لهذا الدور من "
        "«الإعدادات» (الفصل ١٤). وما دامت معطَّلة، تُرفض أي محاولة لتغيير صلاحية استعلام مفرد "
        "ويُسجَّل الرفض في سجل عمليات النظام."))

    h3(doc, T("An Access Manager never sees query text",
              "لا يرى مدير الصلاحيات نص الاستعلام إطلاقًا"))
    para(doc, T(
        "The role reaches the query list and the access pages, but the SQL is removed from "
        "everything sent to it, and the export endpoints — which carry the SQL verbatim — are "
        "administrator-only.",
        "يصل هذا الدور إلى قائمة الاستعلامات وصفحات الصلاحيات، لكن نص SQL يُحذف من كل ما "
        "يُرسَل إليه، كما أن نقاط التصدير — التي تحمل النص حرفيًا — مقصورة على المسؤول."))

    h3(doc, T("An Access Manager cannot touch administrator accounts",
              "لا يستطيع مدير الصلاحيات المساس بحسابات المسؤولين"))
    para(doc, T(
        "A non-administrator cannot modify any account that holds the Admin role, cannot grant "
        "the Admin role, and cannot edit their own roles. The last restriction matters: "
        "without it, an Access Manager could grant themselves the User role and undo the "
        "no-query-access rule in one click.",
        "لا يستطيع غير المسؤول تعديل أي حساب يحمل دور Admin، ولا منح هذا الدور، ولا تعديل "
        "أدواره هو. والقيد الأخير مهم: فبدونه يستطيع مدير الصلاحيات منح نفسه دور User وينقض "
        "قاعدة «لا وصول إلى الاستعلامات» بنقرة واحدة."))
    note(doc, T(
        "An Access Manager can still create a separate account and sign in as it. That is "
        "inherent in one role both creating users and controlling query access — but unlike a "
        "self-grant it leaves an account and an audit entry behind.",
        "يظل بإمكان مدير الصلاحيات إنشاء حساب منفصل وتسجيل الدخول به. وهذا أمر ملازم لكون دور "
        "واحد ينشئ المستخدمين ويتحكم في صلاحيات الاستعلامات — لكنه، بخلاف المنح الذاتي، يترك "
        "خلفه حسابًا وقيدًا في سجل التدقيق."),
        kind="warning")

    h2(doc, T("4.4 What each role sees", "٤.٤ ما يراه كل دور"))
    para(doc, T(
        "The navigation is built from your roles, so the quickest way to tell what an account "
        "can do is to look at its top bar. Compare the administrator's bar in Figure 2 with "
        "the Auditor's below.",
        "يُبنى شريط التنقل من أدوارك، لذا فأسرع طريقة لمعرفة ما يستطيع الحساب فعله هي النظر "
        "إلى شريطه العلوي. قارن شريط المسؤول في الشكل ٢ بشريط المدقق أدناه."))
    figure(doc, "90-auditor-landing",
           T("An Auditor signs in to the execution logs. The bar carries only My Queries, "
             "History, Queries (containing Schedules alone) and Audit — there is no way to "
             "reach the query editor, users or settings.",
             "يصل المدقق بعد تسجيل الدخول إلى سجلات التنفيذ. ولا يحمل الشريط سوى «استعلاماتي» "
             "و«السجل» و«الاستعلامات» (وفيها «المهام المجدولة» وحدها) و«التدقيق» — ولا سبيل "
             "إلى محرر الاستعلامات أو المستخدمين أو الإعدادات."))
    figure(doc, "91-auditor-account-menu",
           T("The Auditor's account menu: language, theme and logout only — no Settings and no "
             "Website logo.",
             "قائمة حساب المدقق: اللغة والنمط وتسجيل الخروج فقط — دون «الإعدادات» ودون «شعار "
             "الموقع»."))
    figure(doc, "93-auditor-my-queries",
           T("My Queries for an Auditor. The page exists but stays empty: the role can never "
             "be granted a query.",
             "صفحة «استعلاماتي» للمدقق. الصفحة موجودة لكنها تبقى فارغة: فلا يمكن إسناد أي "
             "استعلام إلى هذا الدور."))

    h2(doc, T("4.5 How access to a query is decided",
              "٤.٥ كيف يُحدَّد الوصول إلى استعلام"))
    para(doc, T(
        "A user may run a query when at least one of these is true. They add up — there is no "
        "ordering or precedence between them.",
        "يستطيع المستخدم تنفيذ استعلام إذا تحقّق واحد على الأقل مما يلي. وهي تتراكم، ولا "
        "أسبقية بينها."))
    table(doc,
          [T("Route", "الطريق"), T("Granted on", "يُمنح عبر")],
          [
              [T("Role", "الدور"),
               T("The query (or its group) is assigned to a role the user holds.",
                 "الاستعلام (أو مجموعته) مسنَد إلى دور يحمله المستخدم.")],
              [T("Department", "القسم"),
               T("The query (or its group) is assigned to the user's department.",
                 "الاستعلام (أو مجموعته) مسنَد إلى قسم المستخدم.")],
              [T("Named user", "مستخدم بعينه"),
               T("The user is assigned to the query (or its group) individually.",
                 "المستخدم مسنَد إلى الاستعلام (أو مجموعته) بصفة فردية.")],
              [T("Group", "المجموعة"),
               T("Anything granted on a query group applies to every query inside it.",
                 "كل ما يُمنح على مجموعة استعلامات يسري على كل استعلام داخلها.")],
              [T("Administrator", "المسؤول"),
               T("An administrator reaches every query without an assignment.",
                 "يصل المسؤول إلى كل استعلام دون حاجة إلى إسناد.")],
          ],
          widths=[1.5, 5.1])
    note(doc, T(
        "A query must also be Enabled to be runnable, and the user must be allowed to use the "
        "database connection the query runs through (Chapter 11). Both are checked on every "
        "run, not just when the page is opened.",
        "يجب أيضًا أن يكون الاستعلام «نشطًا» ليكون قابلًا للتنفيذ، وأن يكون المستخدم مخوَّلًا "
        "باستخدام اتصال قاعدة البيانات الذي يُنفَّذ الاستعلام من خلاله (الفصل ١١). ويُتحقَّق من "
        "الأمرين في كل تنفيذ، لا عند فتح الصفحة فقط."))

    h2(doc, T("4.6 Page-by-page permission reference",
              "٤.٦ مرجع الصلاحيات صفحةً صفحة"))
    table(doc,
          [T("Page", "الصفحة"), T("Address", "العنوان"), T("Who can open it", "من يفتحها")],
          [
              [T("My Queries", "استعلاماتي"), "/user/queries", T("Everyone", "الجميع")],
              [T("Run a query", "تنفيذ استعلام"), "/user/queries/…/execute",
               T("Everyone (query must be assigned)", "الجميع (بشرط إسناد الاستعلام)")],
              [T("My History", "سجلي"), "/user/history", T("Everyone", "الجميع")],
              [T("My Schedules", "مهامي المجدولة"), "/user/schedules",
               T("Users named as task viewers", "المستخدمون المسمَّون مشاهدين للمهام")],
              [T("Manage Queries", "إدارة الاستعلامات"), "/admin/queries",
               "Admin, AccessManager"],
              [T("Create / edit query", "إنشاء/تعديل استعلام"),
               "/admin/queries/create, /edit/…", "Admin"],
              [T("Manage query access", "إدارة صلاحيات الاستعلام"), "/admin/queries/…/roles",
               T("Admin; AccessManager when enabled", "Admin، وAccessManager عند التفعيل")],
              [T("Query Groups", "مجموعات الاستعلامات"), "/admin/query-groups",
               "Admin, AccessManager"],
              [T("Create / edit group", "إنشاء/تعديل مجموعة"),
               "/admin/query-groups/create, /edit/…", "Admin"],
              [T("Manage group access", "إدارة صلاحيات المجموعة"),
               "/admin/query-groups/…/access", "Admin, AccessManager"],
              [T("Scheduled Tasks", "المهام المجدولة"), "/admin/scheduled-tasks",
               "Admin, Auditor"],
              [T("Create / edit task", "إنشاء/تعديل مهمة"),
               "/admin/scheduled-tasks/create, /edit/…", "Admin"],
              [T("Task run history", "سجل تشغيل المهمة"), "/admin/scheduled-tasks/…/runs",
               "Admin, Auditor"],
              [T("Execution Logs", "سجلات التنفيذ"), "/admin/logs", "Admin, Auditor"],
              [T("System Audit", "سجل عمليات النظام"), "/admin/system-audit", "Admin, Auditor"],
              [T("Settings", "الإعدادات"), "/admin/settings", "Admin"],
              [T("Users", "المستخدمون"), "/admin/users", "Admin, AccessManager"],
              [T("AD Users", "مستخدمو Active Directory"), "/admin/ad-users", "Admin"],
              [T("DB Users", "مستخدمو قواعد البيانات"), "/admin/database-users", "Admin"],
          ],
          widths=[1.8, 2.4, 2.4],
          font_size=8.5)

    page_break(doc)

    # ===================================================================== 5
    h1(doc, T("5. Running queries", "٥. تنفيذ الاستعلامات"))
    para(doc, T("For: Users, and administrators running their own queries.",
                "لمن: المستخدمون، والمسؤولون عند تنفيذ استعلاماتهم."), italic=True)

    h2(doc, T("5.1 My Queries", "٥.١ استعلاماتي"))
    para(doc, T(
        "My Queries lists everything you have been granted, arranged by query group. Queries "
        "that belong to no group appear together at the end. Each card shows the query's name "
        "and description and a button to run it.",
        "تعرض صفحة «استعلاماتي» كل ما أُسنِد إليك، مرتَّبًا حسب مجموعات الاستعلامات. وتظهر "
        "الاستعلامات غير المصنَّفة معًا في النهاية. وتُظهر كل بطاقة اسم الاستعلام ووصفه وزرًا "
        "لتنفيذه."))
    figure(doc, "60-my-queries",
           T("My Queries. Groups act as folders; the count next to each heading is the number "
             "of queries you can reach inside it.",
             "صفحة «استعلاماتي». تعمل المجموعات كمجلدات، والعدد بجانب كل عنوان هو عدد "
             "الاستعلامات التي يمكنك الوصول إليها داخلها."))
    para(doc, T(
        "If the page is empty, nothing has been assigned to you yet — ask an administrator or "
        "an Access Manager to grant the query, the group, or your department.",
        "إذا كانت الصفحة فارغة فلم يُسنَد إليك شيء بعد — اطلب من المسؤول أو مدير الصلاحيات منح "
        "الاستعلام أو المجموعة أو قسمك."))

    h2(doc, T("5.2 Filling in parameters", "٥.٢ تعبئة المعاملات"))
    para(doc, T(
        "Opening a query shows a form built from the parameters the administrator declared. "
        "Required parameters are validated before the Execute button becomes usable.",
        "يعرض فتح الاستعلام نموذجًا مبنيًّا من المعاملات التي عرّفها المسؤول. ويجري التحقق من "
        "المعاملات المطلوبة قبل أن يصبح زر التنفيذ قابلًا للاستخدام."))
    figure(doc, "61-execute-parameters",
           T("A query's parameter form. Dates get a picker, booleans get a switch, and "
             "required fields report their own errors.",
             "نموذج معاملات الاستعلام. تحصل التواريخ على أداة اختيار، والقيم المنطقية على "
             "مفتاح، وتعرض الحقول المطلوبة أخطاءها بنفسها."))
    table(doc,
          [T("Parameter type", "نوع المعامل"), T("How it appears", "كيف يظهر"),
           T("Notes", "ملاحظات")],
          [
              [T("String", "نص"), T("A text box", "حقل نصي"),
               T("Rejects SQL keywords and special characters.",
                 "يرفض كلمات SQL المفتاحية والرموز الخاصة.")],
              [T("Number", "رقم"), T("A numeric box", "حقل رقمي"),
               T("Accepts digits only.", "يقبل الأرقام فقط.")],
              [T("Date", "تاريخ"), T("A box with a calendar picker", "حقل مع تقويم"),
               T("Pick from the calendar or type.", "اختر من التقويم أو اكتب مباشرة.")],
              [T("Boolean", "قيمة منطقية"), T("A switch", "مفتاح تبديل"),
               T("Off unless the administrator set a default.",
                 "معطَّل ما لم يضع المسؤول قيمة افتراضية.")],
              [T("Dropdown", "قائمة منسدلة"), T("A list of choices", "قائمة خيارات"),
               T("Choices are either a fixed list or the live result of another query.",
                 "الخيارات إما قائمة ثابتة أو نتيجة حيّة لاستعلام آخر.")],
          ],
          widths=[1.3, 2.0, 3.3])
    para(doc, T(
        "A parameter may carry a default value, which is filled in for you and can be changed. "
        "Dropdowns whose choices come from a query show a progress bar while they load.",
        "قد يحمل المعامل قيمة افتراضية تُعبَّأ لك ويمكنك تغييرها. وتُظهر القوائم المنسدلة التي "
        "تأتي خياراتها من استعلام شريط تقدّم أثناء التحميل."))

    h2(doc, T("5.3 Multi-value parameters", "٥.٣ المعاملات متعددة القيم"))
    para(doc, T(
        "A String or Dropdown parameter can be marked as accepting several values. A String "
        "parameter then takes a comma-separated list; a Dropdown becomes a multiple-selection "
        "list. Each value is bound separately, so the query matches any of them.",
        "يمكن وسم معامل نصي أو قائمة منسدلة بقبول عدة قيم. فيقبل المعامل النصي حينها قائمة "
        "مفصولة بفواصل، وتصبح القائمة المنسدلة متعددة الاختيار. وتُمرَّر كل قيمة على حدة، "
        "فيطابق الاستعلام أيًّا منها."))
    figure(doc, "64-execute-multivalue",
           T("A multi-value String parameter. The hint under the box explains the "
             "comma-separated format.",
             "معامل نصي متعدد القيم. يشرح التلميح أسفل الحقل صيغة الفصل بالفواصل."))

    h2(doc, T("5.4 Reading the results", "٥.٤ قراءة النتائج"))
    para(doc, T(
        "A read query runs once. The complete result is held on the server, and the grid asks "
        "for one page at a time — so paging, sorting and filtering never re-run the query and "
        "never ship the whole result to your browser.",
        "يُنفَّذ استعلام القراءة مرة واحدة. وتُحفظ النتيجة كاملة على الخادم، ويطلب الجدول صفحة "
        "واحدة في كل مرة — فلا يُعاد تنفيذ الاستعلام عند التصفح أو الترتيب أو التصفية، ولا "
        "تُنقل النتيجة كاملة إلى متصفحك."))
    figure(doc, "62-execute-results",
           T("The result grid. The subtitle reports the row count and how long the query took; "
             "the second header row filters each column.",
             "جدول النتائج. يعرض السطر الفرعي عدد الصفوف ومدة التنفيذ، ويتيح صف الترويسة "
             "الثاني تصفية كل عمود."))
    bullets(doc, [
        (T("Sort — ", "الترتيب — "),
         T("click a column header. Sorting is applied to the whole result, not just the page "
           "on screen.",
           "انقر على ترويسة العمود. ويُطبَّق الترتيب على النتيجة كاملة لا على الصفحة "
           "المعروضة.")),
        (T("Filter a column — ", "تصفية عمود — "),
         T("type in the box under its header. Filters are case-insensitive “contains” matches "
           "and combine across columns.",
           "اكتب في الحقل أسفل ترويسته. والتصفية بحثٌ عن «يحتوي» دون تمييز بين الأحرف الكبيرة "
           "والصغيرة، وتتجمّع عبر الأعمدة.")),
        (T("Page — ", "التصفح — "),
         T("the pager at the bottom; 10, 25, 50 or 100 rows per page.",
           "أداة التصفح في الأسفل: ١٠ أو ٢٥ أو ٥٠ أو ١٠٠ صف في الصفحة.")),
        (T("Row counts — ", "أعداد الصفوف — "),
         T("the subtitle shows the total, and how many rows match the filters when they "
           "differ.",
           "يعرض السطر الفرعي الإجمالي، وعدد الصفوف المطابقة للتصفية عند اختلافهما.")),
    ])
    note(doc, T(
        "Leaving the results page releases the stored result on the server. Use the browser's "
        "Back button and the result is gone — re-run the query to get it back.",
        "مغادرة صفحة النتائج تحرّر النتيجة المخزَّنة على الخادم. فإذا استخدمت زر الرجوع في "
        "المتصفح ضاعت النتيجة — أعد تنفيذ الاستعلام لاستعادتها."))

    h2(doc, T("5.5 Exporting results", "٥.٥ تصدير النتائج"))
    para(doc, T(
        "Export builds the file from the result already held on the server, so exporting after "
        "viewing costs no second run of the query. The whole result is exported, not just the "
        "page on screen.",
        "يبني التصدير الملف من النتيجة المحفوظة على الخادم، فلا يكلّف التصدير بعد العرض تنفيذًا "
        "ثانيًا للاستعلام. وتُصدَّر النتيجة كاملة لا الصفحة المعروضة فقط."))
    figure(doc, "63-export-menu", T("The five export formats.", "صِيَغ التصدير الخمس."))
    table(doc,
          [T("Format", "الصيغة"), T("Use it for", "تُستخدم لـ")],
          [
              ["Excel (.xlsx)",
               T("Further analysis; the default choice for tabular data.",
                 "مزيد من التحليل، وهي الخيار الافتراضي للبيانات الجدولية.")],
              ["CSV (.csv)", T("Loading into another system.", "التحميل إلى نظام آخر.")],
              ["PDF (.pdf)",
               T("Sending a fixed, printable copy.", "إرسال نسخة ثابتة قابلة للطباعة.")],
              ["Word (.docx)",
               T("A formatted report. Uses the query's own Word template when the "
                 "administrator uploaded one, otherwise the system default.",
                 "تقرير منسَّق. يستخدم قالب Word الخاص بالاستعلام إن رفع المسؤول واحدًا، وإلا "
                 "فالقالب الافتراضي للنظام.")],
              ["JSON (.json)", T("Feeding another program.", "تغذية برنامج آخر.")],
          ],
          widths=[1.4, 5.2])

    h2(doc, T("5.6 Long-running queries", "٥.٦ الاستعلامات طويلة التنفيذ"))
    para(doc, T(
        "A query an administrator marked long-running is submitted to run in the background "
        "instead of holding the page open. While it runs you see an elapsed timer, and a "
        "Cancel button appears beside it.",
        "الاستعلام الذي وسمه المسؤول بأنه طويل التنفيذ يُرسَل ليعمل في الخلفية بدل إبقاء "
        "الصفحة منتظرة. وأثناء تنفيذه يظهر مؤقّت للزمن المنقضي، وبجانبه زر إلغاء."))
    para(doc, T(
        "Cancelling is real: it stops the statement on the database rather than merely "
        "abandoning the page. This mode exists so that slow queries are not cut off by a "
        "network gateway; the only limit that still applies is the query's own timeout.",
        "والإلغاء فعلي: فهو يوقف الجملة على قاعدة البيانات لا أن يترك الصفحة فحسب. ووُجد هذا "
        "الوضع كي لا تقطع بوابة الشبكة الاستعلامات البطيئة، ويبقى الحد الوحيد المطبَّق هو مهلة "
        "الاستعلام نفسه."))

    h2(doc, T("5.7 Queries that change data", "٥.٧ الاستعلامات التي تعدّل البيانات"))
    para(doc, T(
        "A query may be an INSERT, UPDATE or DELETE. These run in two phases so you can see "
        "the impact before anything is committed.",
        "قد يكون الاستعلام من نوع INSERT أو UPDATE أو DELETE. وتُنفَّذ هذه على مرحلتين لترى أثر "
        "التغيير قبل اعتماده."))
    figure(doc, "65-execute-write-form",
           T("A write query's form. It looks like any other query; the difference appears "
             "after you run it.",
             "نموذج استعلام تعديل. يبدو كأي استعلام آخر، ويظهر الفرق بعد التنفيذ."))
    figure(doc, "66-execute-write-filled",
           T("The same form with its parameters filled in.",
             "النموذج نفسه بعد تعبئة معاملاته."))
    numbered(doc, [
        T("Press Execute. The statement runs inside a transaction that is then rolled back — "
          "nothing has changed yet.",
          "اضغط «تنفيذ». تُنفَّذ الجملة داخل معاملة يجري التراجع عنها — ولم يتغيّر شيء بعد."),
        T("The confirmation panel reports how many rows would be affected, and for UPDATE and "
          "DELETE lists the actual rows that would change.",
          "تعرض لوحة التأكيد عدد الصفوف التي ستتأثر، وتسرد في حالتَي UPDATE وDELETE الصفوف "
          "الفعلية التي ستتغيّر."),
        T("Press Confirm & Commit to run it for real, or Cancel to walk away. Cancelling "
          "leaves the data exactly as it was.",
          "اضغط «تأكيد واعتماد» للتنفيذ الفعلي، أو «إلغاء» للانصراف. والإلغاء يترك البيانات "
          "كما كانت تمامًا."),
    ])
    figure(doc, "67-execute-write-preview",
           T("The confirmation panel. The row count and the rows themselves come from a real "
             "execution that was rolled back.",
             "لوحة التأكيد. عدد الصفوف والصفوف نفسها ناتجة عن تنفيذ حقيقي جرى التراجع عنه."))
    note(doc, T(
        "Nothing is committed until you press Confirm & Commit. Closing the page at the "
        "preview stage is always safe.",
        "لا يُعتمد أي تغيير حتى تضغط «تأكيد واعتماد». وإغلاق الصفحة في مرحلة المعاينة آمن "
        "دائمًا."))

    h2(doc, T("5.8 Running without the preview", "٥.٨ التنفيذ دون معاينة"))
    para(doc, T(
        "The preview costs two extra round trips and can be slow on large tables. When an "
        "administrator has allowed it for a query, a “Run directly without preview” checkbox "
        "appears above the Execute button. Ticking it commits the change in one pass — the "
        "button turns red and reads Run & Commit, and the note beneath warns that there will "
        "be no confirmation step.",
        "تكلّف المعاينة جولتين إضافيتين وقد تبطؤ على الجداول الكبيرة. وعندما يسمح المسؤول بذلك "
        "لاستعلام ما، يظهر مربع «التنفيذ مباشرة دون معاينة» فوق زر التنفيذ. ووضع علامة عليه "
        "يعتمد التغيير في خطوة واحدة — فيتحول الزر إلى الأحمر ويصبح «تنفيذ واعتماد»، ويحذّر "
        "التنبيه أسفله من غياب خطوة التأكيد."))
    note(doc, T(
        "With the checkbox ticked there is no second chance. Leave it clear unless you run the "
        "same query often and know exactly what it changes.",
        "مع وضع العلامة لا توجد فرصة ثانية. اتركه فارغًا ما لم تكن تنفّذ الاستعلام نفسه كثيرًا "
        "وتعرف بالضبط ما الذي يغيّره."),
        kind="warning")

    h2(doc, T("5.9 Your execution history", "٥.٩ سجل تنفيذك"))
    para(doc, T(
        "History lists every query you have run: when, with which parameters, how long it "
        "took, how many rows came back, and whether it succeeded. A failed run shows the error "
        "message.",
        "يسرد «السجل» كل استعلام نفّذته: متى، وبأي معاملات، وكم استغرق، وكم صفًّا أعاد، وهل "
        "نجح. ويعرض التنفيذ الفاشل رسالة الخطأ."))
    figure(doc, "70-execution-history",
           T("My Execution History. For UPDATE and DELETE runs, the row count is a link that "
             "opens the values as they were before the change.",
             "سجل تنفيذي. في عمليات UPDATE وDELETE يكون عدد الصفوف رابطًا يفتح القيم كما كانت "
             "قبل التغيير."))

    h2(doc, T("5.10 Scheduled task status", "٥.١٠ حالة المهام المجدولة"))
    para(doc, T(
        "If an administrator named you as a viewer of a scheduled task, a Schedules entry "
        "appears in your navigation. It shows each run's start time, status, which trigger "
        "started it, and the files it produced. You can download those files only if you were "
        "also named as a downloader.",
        "إذا سمّاك المسؤول مشاهدًا لمهمة مجدولة ظهر عنصر «المهام المجدولة» في تنقلك. وهو يعرض "
        "وقت بدء كل تشغيل وحالته والمُشغِّل الذي بدأه والملفات التي أنتجها. ولا يمكنك تنزيل تلك "
        "الملفات إلا إذا سُمّيت أيضًا ضمن المخوَّلين بالتنزيل."))

    page_break(doc)

    # ===================================================================== 6
    h1(doc, T("6. Managing queries", "٦. إدارة الاستعلامات"))
    para(doc, T("For: Administrators. Access Managers see the list but cannot create, edit, "
                "delete or export.",
                "لمن: المسؤولون. يرى مديرو الصلاحيات القائمة لكن دون إنشاء أو تعديل أو حذف أو "
                "تصدير."), italic=True)

    h2(doc, T("6.1 The query list", "٦.١ قائمة الاستعلامات"))
    figure(doc, "10-manage-queries",
           T("Manage Queries. Filters across the top, one row per query, and the actions for "
             "each row at the end.",
             "إدارة الاستعلامات. عوامل التصفية في الأعلى، وصف لكل استعلام، وإجراءات كل صف في "
             "طرفه."))
    para(doc, T(
        "The columns are Name, Description, Type, Status, Group, DB User and the number of "
        "parameters. Five filters narrow the list and are remembered when you navigate away "
        "and come back:",
        "الأعمدة هي: الاسم والوصف والنوع والحالة والمجموعة ومستخدم قاعدة البيانات وعدد "
        "المعاملات. وتضيّق خمسة عوامل تصفية القائمة، وتُحفظ عند مغادرة الصفحة والعودة إليها:"))
    bullets(doc, [
        (T("Search — ", "البحث — "),
         T("matches name, description, group and database connection.",
           "يطابق الاسم والوصف والمجموعة واتصال قاعدة البيانات.")),
        (T("Status — ", "الحالة — "), T("All, Active or Disabled.", "الكل أو نشط أو معطَّل.")),
        (T("Type — ", "النوع — "), T("SELECT, INSERT, UPDATE, DELETE or Other.",
                                     "SELECT أو INSERT أو UPDATE أو DELETE أو أخرى.")),
        (T("Group — ", "المجموعة — "), T("one group, or Ungrouped.",
                                         "مجموعة واحدة، أو غير مُصنَّف.")),
        (T("DB User — ", "مستخدم قاعدة البيانات — "),
         T("the database connection the query runs through.",
           "اتصال قاعدة البيانات الذي يُنفَّذ الاستعلام من خلاله.")),
    ])
    para(doc, T(
        "A Clear filters button appears whenever anything is narrowing the list, so a "
        "remembered filter cannot be mistaken for missing queries.",
        "يظهر زر «مسح عوامل التصفية» كلما كان هناك ما يضيّق القائمة، حتى لا تُفهم تصفية محفوظة "
        "على أنها استعلامات مفقودة."))
    table(doc,
          [T("Row action", "إجراء الصف"), T("What it does", "وظيفته"),
           T("Who sees it", "من يراه")],
          [
              [T("Edit", "تعديل"), T("Opens the query for editing", "يفتح الاستعلام للتعديل"),
               "Admin"],
              [T("Copy", "نسخ"),
               T("Opens the create form pre-filled from this query",
                 "يفتح نموذج الإنشاء معبّأً من هذا الاستعلام"), "Admin"],
              [T("Manage access", "إدارة الصلاحيات"),
               T("Assign roles, departments and users", "إسناد الأدوار والأقسام والمستخدمين"),
               T("Admin; AccessManager when enabled in Settings",
                 "Admin، وAccessManager عند تفعيله في الإعدادات")],
              [T("Export", "تصدير"),
               T("Downloads this query as a JSON definition",
                 "ينزّل هذا الاستعلام كتعريف JSON"), "Admin"],
              [T("Delete", "حذف"),
               T("Deletes the query, its parameters and its access assignments",
                 "يحذف الاستعلام ومعاملاته وتخصيصات الوصول الخاصة به"), "Admin"],
          ],
          widths=[1.3, 3.4, 1.9])

    h2(doc, T("6.2 Creating a query", "٦.٢ إنشاء استعلام"))
    figure(doc, "13-query-create",
           T("The Create Dynamic Query form.", "نموذج إنشاء استعلام ديناميكي."))
    table(doc,
          [T("Field", "الحقل"), T("Meaning", "المعنى")],
          [
              [T("Name", "الاسم"),
               T("How the query appears to users. Required.",
                 "الاسم الذي يظهر للمستخدمين. مطلوب.")],
              [T("Description", "الوصف"),
               T("Shown under the name on My Queries. Required.",
                 "يظهر تحت الاسم في صفحة «استعلاماتي». مطلوب.")],
              [T("SQL Query", "استعلام SQL"),
               T("The statement. Write parameters as @name. All statement types are supported.",
                 "نص الجملة. تُكتب المعاملات بالصيغة ‎@name‎. وجميع أنواع الجمل مدعومة.")],
              [T("Timeout (seconds)", "المهلة (بالثواني)"),
               T("How long the query may run. 0 means no limit.",
                 "المدة المسموح بها للتنفيذ. والقيمة ٠ تعني بلا حد.")],
              [T("Long-running query", "استعلام طويل التنفيذ"),
               T("Runs the query as a background job with an elapsed timer and a Cancel "
                 "button. Use for slow queries; leave off otherwise.",
                 "ينفّذ الاستعلام كمهمة خلفية مع مؤقّت وزر إلغاء. يُستخدم للاستعلامات "
                 "البطيئة، ويُترك معطَّلًا فيما عداها.")],
              [T("Allow running without confirmation", "السماح بالتنفيذ دون تأكيد"),
               T("Write queries only. Offers users the “Run directly without preview” "
                 "checkbox. On by default.",
                 "لاستعلامات التعديل فقط. يتيح للمستخدمين مربع «التنفيذ مباشرة دون معاينة». "
                 "وهو مفعَّل افتراضيًا.")],
              [T("Save before-change values", "حفظ القيم قبل التغيير"),
               T("UPDATE and DELETE only. Stores every affected row as it was, with the "
                 "execution log. On by default; turn it off for statements that touch very "
                 "many rows.",
                 "لجملتَي UPDATE وDELETE فقط. يخزّن كل صف متأثر بحالته السابقة مع سجل "
                 "التنفيذ. وهو مفعَّل افتراضيًا، ويُعطَّل للجمل التي تمسّ أعدادًا كبيرة من "
                 "الصفوف.")],
              [T("Database User", "مستخدم قاعدة البيانات"),
               T("Which connection the query runs through. Default uses the system connection.",
                 "الاتصال الذي يُنفَّذ الاستعلام من خلاله. ويستخدم الخيار الافتراضي اتصال "
                 "النظام.")],
              [T("Group", "المجموعة"),
               T("The folder the query appears under on My Queries.",
                 "المجلد الذي يظهر الاستعلام تحته في «استعلاماتي».")],
              [T("Enabled", "نشط"),
               T("Editing only. A disabled query cannot be run by anyone.",
                 "عند التعديل فقط. ولا يستطيع أحد تنفيذ استعلام معطَّل.")],
          ],
          widths=[1.9, 4.7])
    note(doc, T(
        "The statement type is worked out from the SQL when you save, not taken from anything "
        "you choose — so the Type column and the write-query preview can never disagree with "
        "the SQL they describe.",
        "يُستنتج نوع الجملة من نص SQL عند الحفظ، ولا يُؤخذ من اختيارك — فلا يمكن أن يتعارض "
        "عمود «النوع» ولا معاينة استعلام التعديل مع النص الذي يصفانه."))

    h2(doc, T("6.3 Declaring parameters", "٦.٣ تعريف المعاملات"))
    para(doc, T(
        "Add Parameter appends a parameter card. Each one needs a name — the @name used in the "
        "SQL — and a display name, which is the label the user sees.",
        "يضيف زر «إضافة معامل» بطاقة معامل. ويحتاج كل معامل إلى اسم — وهو ‎@name‎ المستخدم في "
        "نص SQL — واسم عرض، وهو التسمية التي يراها المستخدم."))
    figure(doc, "14-query-edit-parameters",
           T("The parameter editor, showing a Dropdown parameter whose choices come from "
             "another query.",
             "محرر المعاملات، ويظهر فيه معامل قائمة منسدلة تأتي خياراته من استعلام آخر."))
    bullets(doc, [
        (T("Type — ", "النوع — "),
         T("String, Number, Date, Boolean or Dropdown.",
           "نص أو رقم أو تاريخ أو قيمة منطقية أو قائمة منسدلة.")),
        (T("Required — ", "مطلوب — "),
         T("the user cannot run the query until it is filled in.",
           "لا يستطيع المستخدم تنفيذ الاستعلام حتى يعبّئه.")),
        (T("Default Value — ", "القيمة الافتراضية — "),
         T("pre-filled for the user, and still editable by them.",
           "تُعبَّأ مسبقًا للمستخدم ويظل بإمكانه تغييرها.")),
        (T("Allow multiple values — ", "السماح بقيم متعددة — "),
         T("String and Dropdown only. The user supplies several values and the query matches "
           "any of them; write the SQL as WHERE col IN (@name).",
           "للنص والقائمة المنسدلة فقط. يزوّد المستخدم عدة قيم فيطابق الاستعلام أيًّا منها، "
           "ويُكتب نص SQL بالصيغة ‎WHERE col IN (@name)‎.")),
    ])
    h3(doc, T("Dropdown parameters", "معاملات القوائم المنسدلة"))
    para(doc, T("A Dropdown parameter draws its choices from one of two sources:",
                "يستمد معامل القائمة المنسدلة خياراته من أحد مصدرين:"))
    bullets(doc, [
        (T("Static list — ", "قائمة ثابتة — "),
         T("label/value pairs you type in. The value is what reaches the SQL; the label is "
           "what the user reads.",
           "أزواج تسمية/قيمة تكتبها بنفسك. فالقيمة هي ما يصل إلى نص SQL، والتسمية هي ما يقرأه "
           "المستخدم.")),
        (T("From database query — ", "من استعلام قاعدة بيانات — "),
         T("pick another stored query and name the column that supplies the value and the "
           "column that supplies the label. The list is fetched fresh each time the form is "
           "opened.",
           "اختر استعلامًا محفوظًا آخر وحدّد العمود الذي يعطي القيمة والعمود الذي يعطي "
           "التسمية. وتُجلب القائمة محدَّثة في كل مرة يُفتح فيها النموذج.")),
    ])

    h2(doc, T("6.4 Word export templates", "٦.٤ قوالب تصدير Word"))
    para(doc, T(
        "When a user exports results to Word, the layout comes from a template. A query can "
        "carry its own; otherwise the system default is used.",
        "عندما يصدّر المستخدم النتائج إلى Word يأتي التنسيق من قالب. وقد يحمل الاستعلام قالبه "
        "الخاص، وإلا استُخدم القالب الافتراضي للنظام."))
    bullets(doc, [
        (T("Per query — ", "لكل استعلام — "),
         T("upload a .docx on the query's edit form. Buttons to download or remove it appear "
           "once one is attached.",
           "ارفع ملف ‎.docx‎ من نموذج تعديل الاستعلام. ويظهر زرّا التنزيل والإزالة بمجرد إرفاق "
           "قالب.")),
        (T("System default — ", "الافتراضي للنظام — "),
         T("the Default Word Template menu on the query list.",
           "قائمة «قالب Word الافتراضي» في صفحة الاستعلامات.")),
    ])
    figure(doc, "12-default-template-menu",
           T("The Default Word Template menu. The line at the top says whether the built-in "
             "layout or an uploaded template is currently in use.",
             "قائمة «قالب Word الافتراضي». ويبيّن السطر العلوي ما إذا كان المستخدَم حاليًا هو "
             "التنسيق المدمج أم قالب مرفوع."))
    para(doc, T("Inside a template, these markers are replaced when the file is produced:",
                "تُستبدل هذه العلامات داخل القالب عند إنشاء الملف:"))
    table(doc,
          [T("Marker", "العلامة"), T("Replaced with", "تُستبدل بـ")],
          [
              ["{{RESULTS}}",
               T("The result table. Put it inside a table to style the output: the row above "
                 "styles the header, the marker's own row styles data rows, and a row below it "
                 "styles alternating rows.",
                 "جدول النتائج. ضعها داخل جدول للتحكم في التنسيق: الصف الذي فوقها ينسّق "
                 "الترويسة، وصفها هي ينسّق صفوف البيانات، والصف الذي تحتها ينسّق الصفوف "
                 "المتناوبة.")],
              ["{{QUERY_NAME}}", T("The query's name.", "اسم الاستعلام.")],
              ["{{GENERATED_AT}}", T("When the file was produced.", "وقت إنشاء الملف.")],
              ["{{ROW_COUNT}}", T("The number of rows.", "عدد الصفوف.")],
              ["{{@paramName}}",
               T("The value the user entered for that parameter.",
                 "القيمة التي أدخلها المستخدم لذلك المعامل.")],
              ["{{PARAMS}}",
               T("Every parameter as “Display Name: value”, one per line.",
                 "كل معامل بصيغة «اسم العرض: القيمة»، واحد في كل سطر.")],
          ],
          widths=[1.7, 4.9])
    para(doc, T("Markers in headers and footers are replaced too.",
                "وتُستبدل العلامات في الرؤوس والتذييلات أيضًا."))

    h2(doc, T("6.5 Backing queries up and restoring them",
              "٦.٥ النسخ الاحتياطي للاستعلامات واستعادتها"))
    figure(doc, "11-backup-menu",
           T("The Backup menu on the query list.",
             "قائمة «نسخة احتياطية» في صفحة الاستعلامات."))
    bullets(doc, [
        (T("Export all queries — ", "تصدير جميع الاستعلامات — "),
         T("downloads every query as one JSON file.",
           "ينزّل كل الاستعلامات في ملف JSON واحد.")),
        (T("Export (row action) — ", "تصدير (إجراء الصف) — "),
         T("downloads a single query as JSON.", "ينزّل استعلامًا واحدًا بصيغة JSON.")),
        (T("Import from backup… — ", "الاستيراد من نسخة احتياطية… — "),
         T("restores from an export file. Administrators only.",
           "يستعيد من ملف تصدير. للمسؤولين فقط.")),
    ])
    para(doc, T(
        "Everything in an export refers to related records by name rather than by internal "
        "identifier, so a backup can be restored onto a different installation:",
        "يشير كل ما في ملف التصدير إلى السجلات المرتبطة بالاسم لا بالمعرّف الداخلي، فيمكن "
        "استعادة النسخة الاحتياطية على تثبيت مختلف:"))
    table(doc,
          [T("Reference", "المرجع"), T("On import", "عند الاستيراد")],
          [
              [T("Query group", "مجموعة الاستعلامات"),
               T("Matched by name, and created if it does not exist.",
                 "تُطابَق بالاسم، وتُنشأ إن لم تكن موجودة.")],
              [T("Database connection", "اتصال قاعدة البيانات"),
               T("Matched by name. If it is missing, the query lands on the default connection "
                 "and a warning is reported.",
                 "يُطابَق بالاسم. وإن كان مفقودًا وُضع الاستعلام على الاتصال الافتراضي مع "
                 "تنبيه.")],
              [T("Roles and users", "الأدوار والمستخدمون"),
               T("Matched by name; assignments that do not match are dropped and reported.",
                 "تُطابَق بالاسم، وتُسقَط التخصيصات غير المطابقة مع الإبلاغ عنها.")],
              [T("Departments", "الأقسام"),
               T("Carried across as written.", "تُنقل كما كُتبت.")],
              [T("Dropdown source query", "استعلام مصدر القائمة المنسدلة"),
               T("Resolved after every query in the file exists, so a dropdown can point at "
                 "another query from the same backup.",
                 "يُحلّ بعد وجود كل استعلامات الملف، فتستطيع القائمة المنسدلة الإشارة إلى "
                 "استعلام آخر من النسخة نفسها.")],
          ],
          widths=[1.7, 4.9])
    note(doc, T(
        "Import never overwrites. A query whose name is already taken is imported as "
        "“Name (imported)”, so a mistaken import is undone by deleting what it added. The "
        "result dialog lists everything imported, everything renamed and every reference that "
        "could not be resolved.",
        "لا يستبدل الاستيراد شيئًا أبدًا. فالاستعلام الذي يحمل اسمًا مستخدَمًا يُستورد باسم "
        "«الاسم (imported)»، فيُلغى الاستيراد الخاطئ بحذف ما أضافه. وتسرد نافذة النتيجة كل ما "
        "استُورد وما أُعيدت تسميته وكل مرجع تعذّر حلّه."))
    note(doc, T(
        "Database passwords are never written to an export file — only the connection's name. "
        "After restoring onto a new installation, create the connection there with its own "
        "credentials and the name match reconnects it.",
        "لا تُكتب كلمات مرور قواعد البيانات في ملف التصدير إطلاقًا — بل اسم الاتصال فقط. وبعد "
        "الاستعادة على تثبيت جديد، أنشئ الاتصال هناك ببيانات اعتماده الخاصة، ويعيد تطابق الاسم "
        "ربطه."),
        kind="warning")

    page_break(doc)

    # ===================================================================== 7
    h1(doc, T("7. Query groups", "٧. مجموعات الاستعلامات"))
    para(doc, T("For: Administrators create and edit them; Access Managers grant access to "
                "them.",
                "لمن: ينشئها المسؤولون ويعدّلونها، ويمنح مديرو الصلاحيات الوصول إليها."),
         italic=True)
    para(doc, T(
        "A query group is a folder. It organises My Queries for users, and — more importantly "
        "— it is the unit of access an Access Manager works with: granting a group grants "
        "every query inside it.",
        "مجموعة الاستعلامات مجلد. تنظّم صفحة «استعلاماتي» للمستخدمين، والأهم أنها وحدة الصلاحية "
        "التي يعمل بها مدير الصلاحيات: فمنح المجموعة يمنح كل استعلام داخلها."))
    figure(doc, "20-query-groups",
           T("Query Groups. The Queries column counts the queries in each group and links to "
             "the list filtered to that group.",
             "مجموعات الاستعلامات. يعدّ عمود «الاستعلامات» ما في كل مجموعة ويرتبط بالقائمة "
             "مُصفّاة على تلك المجموعة."))
    figure(doc, "21-query-group-create",
           T("Creating a group: a name and a description.", "إنشاء مجموعة: اسم ووصف."))
    note(doc, T("Deleting a group does not delete its queries. They stay, and become "
                "ungrouped.",
                "حذف المجموعة لا يحذف استعلاماتها. فهي تبقى وتصبح غير مُصنَّفة."))

    page_break(doc)

    # ===================================================================== 8
    h1(doc, T("8. Granting access", "٨. منح الصلاحيات"))
    para(doc, T("For: Administrators, and Access Managers.",
                "لمن: المسؤولون ومديرو الصلاحيات."), italic=True)

    h2(doc, T("8.1 Access to a single query", "٨.١ صلاحية استعلام مفرد"))
    para(doc, T(
        "The shield icon on a query row opens Manage Query Access, which has three tabs. Each "
        "tab saves independently — the button under a tab saves only that tab.",
        "تفتح أيقونة الدرع في صف الاستعلام صفحة «إدارة صلاحيات الاستعلام»، وفيها ثلاثة "
        "تبويبات. ويُحفظ كل تبويب على حدة — فالزر داخل التبويب يحفظ ذلك التبويب وحده."))
    figure(doc, "15-query-access-roles",
           T("The Roles tab. Auditor and AccessManager are deliberately absent: neither can be "
             "granted a query.",
             "تبويب الأدوار. ولا يظهر Auditor وAccessManager عمدًا: فلا يمكن منح أيٍّ منهما "
             "استعلامًا."))
    figure(doc, "16-query-access-departments",
           T("The Departments tab. Everyone whose account carries that department gets the "
             "query.",
             "تبويب الأقسام. ويحصل على الاستعلام كل من يحمل حسابه ذلك القسم."))
    figure(doc, "17-query-access-users",
           T("The Users tab, for granting the query to named individuals.",
             "تبويب المستخدمين، لمنح الاستعلام لأشخاص بأعيانهم."))

    h2(doc, T("8.2 Access to a group", "٨.٢ صلاحية المجموعة"))
    para(doc, T(
        "Manage Group Access works the same way, with the same three tabs, and applies to "
        "every query in the group. This is the page an Access Manager uses by default.",
        "تعمل صفحة «إدارة صلاحيات المجموعة» بالطريقة نفسها وبالتبويبات الثلاثة نفسها، وتسري "
        "على كل استعلام في المجموعة. وهي الصفحة التي يستخدمها مدير الصلاحيات افتراضيًا."))
    figure(doc, "22-query-group-access",
           T("Manage Group Access.", "إدارة صلاحيات المجموعة."))

    h2(doc, T("8.3 How the grants combine", "٨.٣ كيف تتجمّع الأذونات"))
    para(doc, T(
        "Grants add up and there is no “deny”. A user reaches a query if any one of the "
        "following is true: a role they hold is granted the query or its group; their "
        "department is granted it; they are granted it by name; or they are an administrator. "
        "Removing one route leaves the others in place — to remove access completely, check "
        "all three tabs on both the query and its group.",
        "تتراكم الأذونات ولا يوجد «منع». فيصل المستخدم إلى الاستعلام إذا تحقّق أي مما يلي: أن "
        "يكون دور يحمله ممنوحًا الاستعلام أو مجموعته، أو أن يكون قسمه ممنوحًا إياه، أو أن "
        "يُمنح باسمه، أو أن يكون مسؤولًا. وإزالة طريق واحد تبقي البقية قائمة — ولإزالة "
        "الصلاحية تمامًا راجع التبويبات الثلاثة على الاستعلام وعلى مجموعته معًا."))

    page_break(doc)

    # ===================================================================== 9
    h1(doc, T("9. Scheduled tasks", "٩. المهام المجدولة"))
    para(doc, T("For: Administrators create and run them; Auditors read them; named users see "
                "their status.",
                "لمن: ينشئها المسؤولون ويشغّلونها، ويطّلع عليها المدققون، ويرى المستخدمون "
                "المسمَّون حالتها."), italic=True)
    para(doc, T(
        "A scheduled task runs one or more queries unattended on a timetable and writes the "
        "results to files on the server.",
        "تنفّذ المهمة المجدولة استعلامًا أو أكثر تلقائيًا وفق جدول زمني، وتكتب النتائج في ملفات "
        "على الخادم."))

    h2(doc, T("9.1 The task list", "٩.١ قائمة المهام"))
    figure(doc, "30-scheduled-tasks",
           T("Scheduled Tasks, with the next and last run times for each task.",
             "المهام المجدولة، مع وقتَي التشغيل التالي والأخير لكل مهمة."))
    bullets(doc, [
        (T("Run now — ", "تشغيل الآن — "),
         T("starts the task immediately without waiting for a trigger.",
           "يبدأ المهمة فورًا دون انتظار مُشغِّل.")),
        (T("Run history — ", "سجل التشغيل — "),
         T("opens the record of past runs.", "يفتح سجل عمليات التشغيل السابقة.")),
        (T("Edit / Delete — ", "تعديل/حذف — "),
         T("administrators only. Deleting a task also deletes its run history.",
           "للمسؤولين فقط. وحذف المهمة يحذف سجل تشغيلها أيضًا.")),
        (T("Enabled — ", "مفعَّلة — "),
         T("a disabled task keeps its definition but no trigger fires.",
           "المهمة المعطَّلة تحتفظ بتعريفها لكن لا يعمل أي مُشغِّل.")),
    ])

    h2(doc, T("9.2 Creating a task", "٩.٢ إنشاء مهمة"))
    figure(doc, "31-scheduled-task-create",
           T("The Create Scheduled Task form.", "نموذج إنشاء مهمة مجدولة."))
    h3(doc, T("Task", "المهمة"))
    table(doc,
          [T("Field", "الحقل"), T("Meaning", "المعنى")],
          [
              [T("Name", "الاسم"), T("Identifies the task. Required.", "يعرّف المهمة. مطلوب.")],
              [T("Output folder", "مجلد المخرجات"),
               T("Absolute path on the server where files are written. Created automatically "
                 "if it does not exist. Required.",
                 "مسار مطلق على الخادم تُكتب فيه الملفات. ويُنشأ تلقائيًا إن لم يكن موجودًا. "
                 "مطلوب.")],
              [T("Archive folder", "مجلد الأرشيف"),
               T("Optional. A copy of every file is also written here.",
                 "اختياري. وتُكتب فيه نسخة من كل ملف أيضًا.")],
              [T("Combine all results into one file", "دمج كل النتائج في ملف واحد"),
               T("Appends every query's result into a single file, in query order.",
                 "يلحق نتيجة كل استعلام في ملف واحد، بترتيب الاستعلامات.")],
              [T("Include header row", "تضمين صف الترويسة"),
               T("Off means CSV and Excel files contain data rows only.",
                 "تعطيله يعني أن ملفات CSV وExcel تحتوي على صفوف البيانات فقط.")],
              [T("Timestamp format", "صيغة الطابع الزمني"),
               T("A .NET date format appended to file names, e.g. -yyyy-MM-dd. Without a "
                 "timestamp the same file is overwritten on every run.",
                 "صيغة تاريخ ‎.NET‎ تُلحق بأسماء الملفات، مثل ‎-yyyy-MM-dd‎. وبدون طابع زمني "
                 "يُستبدل الملف نفسه في كل تشغيل.")],
          ],
          widths=[1.9, 4.7])

    h3(doc, T("Schedule", "الجدولة"))
    para(doc, T(
        "A task can carry several triggers and runs on every one of them — for example daily "
        "at 03:00 plus monthly on day 14. Each trigger is one of:",
        "قد تحمل المهمة عدة مُشغِّلات وتعمل عند كل منها — مثلًا يوميًا الساعة ٠٣:٠٠ وشهريًا في "
        "اليوم ١٤. وكل مُشغِّل من الأنواع التالية:"))
    table(doc,
          [T("Frequency", "التكرار"), T("Additional settings", "إعدادات إضافية")],
          [
              [T("Every N minutes", "كل N دقيقة"), T("Interval in minutes", "الفاصل بالدقائق")],
              [T("Daily", "يوميًا"), T("Time of day", "وقت اليوم")],
              [T("Weekly", "أسبوعيًا"), T("Day of week and time of day",
                                          "يوم الأسبوع ووقت اليوم")],
              [T("Monthly", "شهريًا"), T("Day of month and time of day",
                                         "يوم الشهر ووقت اليوم")],
          ],
          widths=[1.9, 4.7])

    h3(doc, T("Queries to export", "الاستعلامات المراد تصديرها"))
    para(doc, T("Add one row per query. For a read query you also choose:",
                "أضف صفًّا لكل استعلام. ولاستعلام القراءة تختار أيضًا:"))
    bullets(doc, [
        (T("Format — ", "الصيغة — "),
         T("the export format for that query's file (with a separator option for CSV).",
           "صيغة تصدير ملف ذلك الاستعلام (مع خيار الفاصل في CSV).")),
        (T("File name — ", "اسم الملف — "),
         T("defaults to the query's name.", "يأخذ اسم الاستعلام افتراضيًا.")),
        (T("Append timestamp — ", "إلحاق طابع زمني — "),
         T("on to keep each run's file; off to overwrite one file.",
           "تفعيله يحفظ ملف كل تشغيل، وتعطيله يستبدل ملفًا واحدًا.")),
        (T("Incremental run — ", "التشغيل التراكمي — "),
         T("optional. Name a key column, the parameter that receives it, and a starting value: "
           "each run passes the highest key from the previous run into that parameter, so only "
           "new rows are exported. Order the query by the key column ascending.",
           "اختياري. حدّد عمود المفتاح، والمعامل الذي يستقبله، وقيمة البداية: يمرّر كل تشغيل "
           "أعلى مفتاح من التشغيل السابق إلى ذلك المعامل، فتُصدَّر الصفوف الجديدة فقط. ورتّب "
           "الاستعلام تصاعديًا حسب عمود المفتاح.")),
        (T("Parameter values — ", "قيم المعاملات — "),
         T("fixed values for the query's parameters, since there is no one present to type "
           "them.",
           "قيم ثابتة لمعاملات الاستعلام، إذ لا يوجد من يكتبها وقت التشغيل.")),
    ])
    note(doc, T(
        "A write query can be scheduled too. It executes and commits on every run — there is "
        "no preview step and no output file, only the affected-row count in the run status.",
        "يمكن جدولة استعلام تعديل أيضًا. وهو يُنفَّذ ويُعتمد في كل تشغيل — دون خطوة معاينة ودون "
        "ملف مخرجات، وإنما عدد الصفوف المتأثرة في حالة التشغيل فقط."),
        kind="warning")
    figure(doc, "32-scheduled-task-edit",
           T("An existing task, showing its triggers, its queries and their parameter values.",
             "مهمة قائمة، وتظهر فيها مُشغِّلاتها واستعلاماتها وقيم معاملاتها."))

    h3(doc, T("Status visibility", "ظهور الحالة"))
    para(doc, T(
        "Two lists at the bottom of the form decide who, outside the administrators, can see "
        "the task:",
        "تحدّد قائمتان في أسفل النموذج مَن يرى المهمة من غير المسؤولين:"))
    bullets(doc, [
        (T("Users who can view this task's status — ",
           "المستخدمون الذين يمكنهم عرض حالة هذه المهمة — "),
         T("these users get the Schedules page and see the task's runs. Administrators and "
           "Auditors always see every task.",
           "يحصل هؤلاء على صفحة «المهام المجدولة» ويرون عمليات تشغيل المهمة. ويرى المسؤولون "
           "والمدققون كل المهام دائمًا.")),
        (T("Viewers who can also download the output files — ",
           "المشاهدون الذين يمكنهم أيضًا تنزيل ملفات المخرجات — "),
         T("chosen from the viewers above. Administrators can always download; Auditors never "
           "can.",
           "يُختارون من المشاهدين أعلاه. ويستطيع المسؤولون التنزيل دائمًا، ولا يستطيعه "
           "المدققون إطلاقًا.")),
    ])

    h2(doc, T("9.3 Run history", "٩.٣ سجل التشغيل"))
    figure(doc, "33-scheduled-task-runs",
           T("Run history. Each run expands to show the queries it executed, the files it "
             "wrote, row counts, durations and any error.",
             "سجل التشغيل. ويتوسّع كل تشغيل ليعرض الاستعلامات التي نُفِّذت والملفات التي "
             "كُتبت وأعداد الصفوف والمدد وأي خطأ."))
    bullets(doc, [
        (T("Download — ", "تنزيل — "),
         T("fetches a file the run produced. If the file has since been moved, deleted or "
           "overwritten by a later run, the download reports that instead.",
           "يجلب ملفًا أنتجه التشغيل. وإذا كان الملف قد نُقل أو حُذف أو استُبدل بتشغيل لاحق "
           "أبلغك التنزيل بذلك.")),
        (T("Cancel — ", "إلغاء — "),
         T("appears on a run that is still going, and stops the running query on the database. "
           "Administrators only.",
           "يظهر على التشغيل الجاري، ويوقف الاستعلام المنفَّذ على قاعدة البيانات. للمسؤولين "
           "فقط.")),
        (T("Checkpoint — ", "نقطة التوقف — "),
         T("for an incremental query, the key value the run finished at, which the next run "
           "starts from.",
           "في الاستعلام التراكمي، هي قيمة المفتاح التي انتهى عندها التشغيل ويبدأ منها "
           "التشغيل التالي.")),
    ])
    note(doc, T("Only one run panel is open at a time — opening another closes the first.",
                "لا تُفتح إلا لوحة تشغيل واحدة في كل مرة — وفتح أخرى يغلق الأولى."))

    page_break(doc)

    # ===================================================================== 10
    h1(doc, T("10. Users and Active Directory", "١٠. المستخدمون وActive Directory"))
    para(doc, T("For: Administrators. Access Managers can use the Users page with "
                "restrictions.",
                "لمن: المسؤولون. ويستطيع مديرو الصلاحيات استخدام صفحة المستخدمين بقيود."),
         italic=True)

    h2(doc, T("10.1 User management", "١٠.١ إدارة المستخدمين"))
    figure(doc, "51-user-management",
           T("User Management. The list shows each account's roles, whether it is local or "
             "came from Active Directory, and whether it is active.",
             "إدارة المستخدمين. تعرض القائمة أدوار كل حساب، وهل هو محلي أم من Active "
             "Directory، وهل هو نشط."))
    figure(doc, "52-user-create-form",
           T("Creating a local user. Email is optional; a department here is what "
             "department-based query access matches on.",
             "إنشاء مستخدم محلي. البريد الإلكتروني اختياري، والقسم هنا هو ما تطابقه صلاحيات "
             "الاستعلامات المبنية على الأقسام."))
    table(doc,
          [T("Action", "الإجراء"), T("Effect", "الأثر")],
          [
              [T("Create User", "إنشاء مستخدم"),
               T("Adds a local account with a password, department and roles.",
                 "يضيف حسابًا محليًا بكلمة مرور وقسم وأدوار.")],
              [T("Edit", "تعديل"),
               T("Changes the username, the roles, or both.",
                 "يغيّر اسم المستخدم أو الأدوار أو كليهما.")],
              [T("Reset password", "إعادة تعيين كلمة المرور"),
               T("Replaces the account's password with a temporary one you must pass on. The "
                 "old password stops working immediately.",
                 "يستبدل كلمة مرور الحساب بأخرى مؤقتة عليك تسليمها للمستخدم. وتتوقف كلمة "
                 "المرور القديمة فورًا.")],
              [T("Deactivate / Activate", "تعطيل/تفعيل"),
               T("A deactivated account is signed out and blocked from signing in until "
                 "reactivated.",
                 "يُخرَج الحساب المعطَّل من الجلسة ويُمنع من الدخول حتى يُعاد تفعيله.")],
          ],
          widths=[1.7, 4.9])
    note(doc, T(
        "An Access Manager may use this page but cannot modify any account holding the Admin "
        "role, cannot grant the Admin role, and cannot change their own roles. An attempt is "
        "refused and the refusal is recorded in the system audit trail.",
        "يستطيع مدير الصلاحيات استخدام هذه الصفحة، لكنه لا يعدّل أي حساب يحمل دور Admin، ولا "
        "يمنح دور Admin، ولا يغيّر أدواره هو. وتُرفض أي محاولة ويُسجَّل الرفض في سجل عمليات "
        "النظام."),
        kind="warning")

    h2(doc, T("10.2 Active Directory", "١٠.٢ Active Directory"))
    para(doc, T(
        "AD Users imports accounts from the directory so they can be granted queries. It has "
        "three tabs.",
        "تستورد صفحة «مستخدمو Active Directory» الحسابات من الدليل ليتسنى منحها استعلامات، "
        "وفيها ثلاثة تبويبات."))
    figure(doc, "55-ad-users-search",
           T("Search Users. Type at least two characters, tick the accounts to import, then "
             "press Import Selected Users. Already-imported accounts cannot be selected again.",
             "بحث المستخدمين. اكتب حرفين على الأقل، وعلّم الحسابات المراد استيرادها، ثم اضغط "
             "«استيراد المستخدمين المحددين». ولا يمكن تحديد الحسابات المستوردة مسبقًا مرة "
             "أخرى."))
    figure(doc, "56-ad-users-departments",
           T("Departments. View a department's members, or import all of them at once.",
             "الأقسام. اعرض أعضاء قسم ما، أو استوردهم جميعًا دفعة واحدة."))
    figure(doc, "57-ad-users-imported",
           T("Imported Users — the accounts already in the system, with Sync from AD to "
             "refresh their details.",
             "المستخدمون المستوردون — الحسابات الموجودة في النظام، مع «مزامنة من AD» لتحديث "
             "بياناتهم."))
    bullets(doc, [
        (T("Sync from AD — ", "مزامنة من AD — "),
         T("refreshes names, emails and departments for accounts already imported.",
           "يحدّث الأسماء والبُرد الإلكترونية والأقسام للحسابات المستوردة.")),
        (T("Revoke access — ", "سحب الوصول — "),
         T("an imported user immediately loses access to the application. Reversible from the "
           "same page.",
           "يفقد المستخدم المستورد وصوله إلى التطبيق فورًا. ويمكن التراجع من الصفحة نفسها.")),
        (T("Restore access — ", "استعادة الوصول — "),
         T("puts a revoked account back.", "يعيد الحساب المسحوب منه الوصول.")),
    ])

    page_break(doc)

    # ===================================================================== 11
    h1(doc, T("11. Database connections", "١١. اتصالات قواعد البيانات"))
    para(doc, T("For: Administrators only.", "لمن: المسؤولون فقط."), italic=True)
    para(doc, T(
        "A database connection — shown as a “DB User” — is the set of credentials a query runs "
        "through. Keeping several lets different queries reach different databases, or the "
        "same database with different privileges.",
        "اتصال قاعدة البيانات — ويظهر باسم «مستخدم قاعدة البيانات» — هو مجموعة بيانات الاعتماد "
        "التي يُنفَّذ الاستعلام من خلالها. والاحتفاظ بعدة اتصالات يتيح لاستعلامات مختلفة الوصول "
        "إلى قواعد بيانات مختلفة، أو إلى القاعدة نفسها بصلاحيات مختلفة."))
    figure(doc, "53-database-users",
           T("The database connections list.", "قائمة اتصالات قواعد البيانات."))
    figure(doc, "54-database-user-form",
           T("Creating a connection. The fields shown depend on the database type.",
             "إنشاء اتصال. وتعتمد الحقول المعروضة على نوع قاعدة البيانات."))
    table(doc,
          [T("Field", "الحقل"), T("Meaning", "المعنى")],
          [
              [T("Name", "الاسم"),
               T("A friendly name, e.g. “Production read-only”. This is the name a query "
                 "refers to, and the name carried in an export file.",
                 "اسم واضح، مثل «إنتاج للقراءة فقط». وهو الاسم الذي يشير إليه الاستعلام "
                 "والاسم الذي يُحمل في ملف التصدير.")],
              [T("Database Type", "نوع قاعدة البيانات"),
               "Oracle، SQL Server، MySQL، PostgreSQL" if is_rtl()
               else "Oracle, SQL Server, MySQL or PostgreSQL."],
              [T("Host / Port", "المضيف/المنفذ"),
               T("Where the server is.", "موقع الخادم.")],
              [T("Service Name / Database Name", "اسم الخدمة/اسم قاعدة البيانات"),
               T("Which database — the field shown depends on the type.",
                 "أي قاعدة بيانات — والحقل المعروض يعتمد على النوع.")],
              [T("DB Username / Password", "اسم المستخدم/كلمة المرور"),
               T("The credentials. The password is encrypted at rest and, when editing, is "
                 "left unchanged if you leave the box blank.",
                 "بيانات الاعتماد. وكلمة المرور مشفَّرة عند التخزين، وتبقى دون تغيير عند "
                 "التعديل إذا تركت الحقل فارغًا.")],
          ],
          widths=[2.1, 4.5])
    bullets(doc, [
        (T("Test Connection — ", "اختبار الاتصال — "),
         T("checks the settings against the real server before you save.",
           "يتحقق من الإعدادات مقابل الخادم الفعلي قبل الحفظ.")),
        (T("Manage Access — ", "إدارة الصلاحيات — "),
         T("chooses which roles may execute queries that use this connection. A user needs "
           "access both to the query and to its connection.",
           "يحدّد الأدوار التي يمكنها تنفيذ الاستعلامات التي تستخدم هذا الاتصال. ويحتاج "
           "المستخدم إلى صلاحية على الاستعلام وعلى اتصاله معًا.")),
        (T("Copy — ", "نسخ — "),
         T("creates a new connection pre-filled from this one.",
           "ينشئ اتصالًا جديدًا معبّأً من هذا الاتصال.")),
        (T("Delete — ", "حذف — "),
         T("any query using the connection falls back to the default system connection.",
           "يعود أي استعلام يستخدم الاتصال إلى اتصال النظام الافتراضي.")),
    ])

    page_break(doc)

    # ===================================================================== 12
    h1(doc, T("12. Audit trails", "١٢. سجلات التدقيق"))
    para(doc, T("For: Administrators and Auditors.", "لمن: المسؤولون والمدققون."), italic=True)
    para(doc, T(
        "There are two separate trails, and the distinction matters: the execution logs answer "
        "what people ran, and the system audit answers what people changed.",
        "هناك سجلّان منفصلان، والتمييز بينهما مهم: تجيب سجلات التنفيذ عن سؤال «ماذا نفّذ "
        "الناس»، ويجيب سجل عمليات النظام عن سؤال «ماذا غيّر الناس»."))

    h2(doc, T("12.1 Execution logs", "١٢.١ سجلات التنفيذ"))
    para(doc, T(
        "Every execution writes a row — successes, failures and preview attempts alike. The "
        "list filters, sorts and pages on the server, so it stays fast on a large history.",
        "يكتب كل تنفيذ صفًّا — سواء نجح أم فشل أم كان معاينة. وتجري التصفية والترتيب والتقسيم "
        "إلى صفحات على الخادم، فتبقى القائمة سريعة مع كِبَر السجل."))
    figure(doc, "40-execution-logs",
           T("Execution Logs. Each row records the query, its type, who ran it, the "
             "parameters, when, how long it took, how many rows and whether it succeeded.",
             "سجلات التنفيذ. يسجّل كل صف الاستعلام ونوعه ومن نفّذه والمعاملات ووقت التنفيذ "
             "ومدته وعدد الصفوف وهل نجح."))
    bullets(doc, [
        (T("Search — ", "البحث — "),
         T("matches query name, user, parameters and error text.",
           "يطابق اسم الاستعلام والمستخدم والمعاملات ونص الخطأ.")),
        (T("Status — ", "الحالة — "),
         T("successful or failed runs.", "عمليات التنفيذ الناجحة أو الفاشلة.")),
        (T("Type — ", "النوع — "),
         T("SELECT, INSERT, UPDATE, DELETE or Other.",
           "SELECT أو INSERT أو UPDATE أو DELETE أو أخرى.")),
        (T("Before-change values — ", "قيم ما قبل التغيير — "),
         T("for an UPDATE or DELETE run, the row count is a link that opens the affected rows "
           "as they were before the change. It is available only when the query had “Save "
           "before-change values” switched on at the time.",
           "في عمليات UPDATE وDELETE يكون عدد الصفوف رابطًا يفتح الصفوف المتأثرة بحالتها قبل "
           "التغيير. وهي متاحة فقط إذا كان خيار «حفظ القيم قبل التغيير» مفعَّلًا حينها.")),
    ])

    h2(doc, T("12.2 System audit", "١٢.٢ سجل عمليات النظام"))
    para(doc, T(
        "Every administrative change is recorded here: accounts created, permissions granted, "
        "queries edited, tasks run, settings changed.",
        "يُسجَّل هنا كل تغيير إداري: إنشاء الحسابات، ومنح الصلاحيات، وتعديل الاستعلامات، "
        "وتشغيل المهام، وتغيير الإعدادات."))
    figure(doc, "41-system-audit",
           T("System Audit. Filter by category and by outcome; Details expands the recorded "
             "values for an entry.",
             "سجل عمليات النظام. صفِّ حسب الفئة والنتيجة، ويعرض «التفاصيل» القيم المسجَّلة "
             "للقيد."))
    bullets(doc, [
        (T("Categories — ", "الفئات — "),
         T("Users, Queries, Query groups, Permissions, Database connections, Scheduled tasks, "
           "Active Directory, Branding, Settings, Other.",
           "المستخدمون، والاستعلامات، ومجموعات الاستعلامات، والصلاحيات، واتصالات قواعد "
           "البيانات، والمهام المجدولة، وActive Directory، والهوية البصرية، والإعدادات، "
           "وأخرى.")),
        (T("Outcome — ", "النتيجة — "),
         T("Succeeded or Refused. Refusals are recorded deliberately — “an Access Manager "
           "tried to reset an administrator's password and was refused” is often the entry "
           "that matters.",
           "نجحت أو رُفضت. وتُسجَّل حالات الرفض عمدًا — فقيد «حاول مدير صلاحيات إعادة تعيين "
           "كلمة مرور مسؤول فرُفض» هو غالبًا القيد المهم.")),
        (T("Details — ", "التفاصيل — "),
         T("the values submitted with the change.", "القيم المرسَلة مع التغيير.")),
    ])
    note(doc, T(
        "The trail is read-only through the application: there is no way to edit or delete an "
        "entry. Passwords, tokens, connection strings and similar values are replaced with "
        "*** before an entry is stored, and uploaded file contents are omitted.",
        "السجل للقراءة فقط من خلال التطبيق: فلا سبيل إلى تعديل قيد أو حذفه. وتُستبدل كلمات "
        "المرور والرموز وسلاسل الاتصال وما شابهها بـ *** قبل تخزين القيد، وتُحذف محتويات "
        "الملفات المرفوعة."))
    note(doc, T(
        "There is no automatic pruning — the table only grows. Plan a housekeeping job for a "
        "long-lived installation.",
        "لا توجد عملية تنظيف تلقائية — فالجدول ينمو باستمرار. خطّط لمهمة صيانة دورية في "
        "التثبيتات طويلة العمر."),
        kind="warning")
    figure(doc, "92-auditor-system-audit",
           T("The same page seen by an Auditor. The content is identical; the navigation "
             "around it is not.",
             "الصفحة نفسها كما يراها المدقق. المحتوى مطابق، أما التنقل المحيط به فلا."))

    page_break(doc)

    # ===================================================================== 13
    h1(doc, T("13. The website logo", "١٣. شعار الموقع"))
    para(doc, T("For: Administrators only.", "لمن: المسؤولون فقط."), italic=True)
    para(doc, T(
        "The top bar shows an uploaded logo in place of the application name. Open the account "
        "menu and choose Website logo.",
        "يعرض الشريط العلوي الشعار المرفوع بدل اسم التطبيق. افتح قائمة الحساب واختر «شعار "
        "الموقع»."))
    figure(doc, "58-logo-dialog",
           T("The logo dialog, with a preview and the recommended dimensions.",
             "نافذة الشعار، وفيها معاينة والأبعاد الموصى بها."))
    bullets(doc, [
        (T("Formats — ", "الصيغ — "),
         T("PNG, JPG or WebP, up to 1 MB. SVG is not accepted.",
           "PNG أو JPG أو WebP بحد أقصى ١ ميغابايت. ولا تُقبل صيغة SVG.")),
        (T("Size — ", "الحجم — "),
         T("the bar renders the logo 40 pixels tall; 80 pixels tall is recommended so it stays "
           "sharp on high-resolution screens. Width is capped at 200 pixels.",
           "يعرض الشريط الشعار بارتفاع ٤٠ بكسل، ويُنصح بارتفاع ٨٠ بكسل ليبقى واضحًا على الشاشات "
           "عالية الدقة. والعرض محدود بـ ٢٠٠ بكسل.")),
        (T("Warnings — ", "التنبيهات — "),
         T("the dialog warns before uploading if the image is too short, below the recommended "
           "resolution, or so wide it will be cropped by the cap.",
           "تحذّرك النافذة قبل الرفع إذا كانت الصورة قصيرة أو دون الدقة الموصى بها أو عريضة "
           "لدرجة يقتصّها الحد.")),
        (T("Remove — ", "إزالة — "),
         T("with no logo stored, the bar falls back to the application name, so it is never "
           "blank.",
           "إذا لم يكن هناك شعار مخزَّن عاد الشريط إلى اسم التطبيق، فلا يبقى فارغًا أبدًا.")),
    ])
    note(doc, T(
        "Remove takes effect at once, with no confirmation step. Keep a copy of the logo file "
        "before you open this dialog.",
        "تسري «الإزالة» فورًا دون خطوة تأكيد. احتفظ بنسخة من ملف الشعار قبل فتح هذه النافذة."),
        kind="warning")

    # ===================================================================== 14
    h1(doc, T("14. System settings", "١٤. إعدادات النظام"))
    para(doc, T("For: Administrators only.", "لمن: المسؤولون فقط."), italic=True)
    para(doc, T(
        "Settings holds toggles that change what a whole role is permitted to do. They take "
        "effect immediately, without a restart, and each change is recorded in the system "
        "audit trail.",
        "تضم «الإعدادات» مفاتيح تغيّر ما يُسمح لدور كامل بفعله. وتسري فورًا دون إعادة تشغيل، "
        "ويُسجَّل كل تغيير في سجل عمليات النظام."))
    figure(doc, "50-system-settings", T("System Settings.", "إعدادات النظام."))
    table(doc,
          [T("Setting", "الإعداد"), T("Default", "الافتراضي"), T("Effect", "الأثر")],
          [
              [T("Access Managers can manage individual query access",
                 "يمكن لمديري الصلاحيات إدارة صلاحيات الاستعلامات المفردة"),
               T("Off", "معطَّل"),
               T("When on, Access Managers may assign roles, departments and users to a single "
                 "query as well as to a query group. Administrators are unaffected either way.",
                 "عند تفعيله يستطيع مديرو الصلاحيات إسناد الأدوار والأقسام والمستخدمين إلى "
                 "استعلام مفرد إضافةً إلى مجموعة الاستعلامات. ولا يتأثر المسؤولون في "
                 "الحالتين.")],
          ],
          widths=[2.2, 0.8, 3.6])
    note(doc, T(
        "While the setting is off, an Access Manager's attempt to change per-query access is "
        "refused and recorded as a refusal in the system audit trail.",
        "ما دام الإعداد معطَّلًا، تُرفض محاولة مدير الصلاحيات تغيير صلاحية استعلام مفرد "
        "وتُسجَّل بوصفها رفضًا في سجل عمليات النظام."))

    page_break(doc)

    # ===================================================================== 15
    h1(doc, T("15. Appendix A — Frequently asked questions",
              "١٥. الملحق أ — أسئلة شائعة"))
    table(doc,
          [T("Question", "السؤال"), T("Answer", "الجواب")],
          [
              [T("My Queries is empty.", "صفحة «استعلاماتي» فارغة."),
               T("Nothing has been assigned to you. Ask an administrator or Access Manager to "
                 "grant you the query, its group, or your department.",
                 "لم يُسنَد إليك شيء. اطلب من المسؤول أو مدير الصلاحيات منحك الاستعلام أو "
                 "مجموعته أو قسمك.")],
              [T("I can see a query but cannot run it.",
                 "أرى استعلامًا لكنني لا أستطيع تنفيذه."),
               T("It may be disabled, or you may not have access to the database connection it "
                 "uses. Both are checked at run time.",
                 "قد يكون معطَّلًا، أو قد لا تملك صلاحية على اتصال قاعدة البيانات الذي "
                 "يستخدمه. ويُتحقَّق من الأمرين وقت التنفيذ.")],
              [T("I pressed Back and my results are gone.",
                 "ضغطت «رجوع» فاختفت نتائجي."),
               T("Leaving the results page releases the stored result on the server. Re-run "
                 "the query.",
                 "مغادرة صفحة النتائج تحرّر النتيجة المخزَّنة على الخادم. أعد تنفيذ "
                 "الاستعلام.")],
              [T("Export only gave me part of the result.",
                 "التصدير أعطاني جزءًا من النتيجة فقط."),
               T("It does not — export always writes the complete result, not the page on "
                 "screen. The grid shows one page at a time by design.",
                 "بل لا يفعل — فالتصدير يكتب النتيجة كاملة دائمًا لا الصفحة المعروضة. ويعرض "
                 "الجدول صفحة واحدة في كل مرة بحكم التصميم.")],
              [T("I confirmed a write query by mistake.",
                 "أكّدت استعلام تعديل بالخطأ."),
               T("The change is committed. Use the execution log's before-change values to see "
                 "what the rows held previously.",
                 "التغيير معتمَد. استخدم «قيم ما قبل التغيير» في سجل التنفيذ لمعرفة ما كانت "
                 "تحمله الصفوف سابقًا.")],
              [T("A scheduled file will not download.", "ملف مهمة مجدولة لا يُنزَّل."),
               T("The file was moved, deleted, or overwritten by a later run. Run the task "
                 "again, or check the output folder on the server.",
                 "نُقل الملف أو حُذف أو استُبدل بتشغيل لاحق. شغّل المهمة مجددًا أو تحقّق من "
                 "مجلد المخرجات على الخادم.")],
              [T("I am an Auditor and cannot run anything.",
                 "أنا مدقق ولا أستطيع تنفيذ أي شيء."),
               T("That is by design. The Auditor role can never be granted query access. If "
                 "you also need to run queries, ask for the User role in addition.",
                 "هذا بحكم التصميم. فلا يمكن منح دور Auditor صلاحية على الاستعلامات إطلاقًا. "
                 "وإذا احتجت إلى التنفيذ أيضًا فاطلب إضافة دور User.")],
              [T("My imported query is named “… (imported)”.",
                 "استعلامي المستورد يحمل اسم «… (imported)»."),
               T("A query of that name already existed. Import never overwrites — reconcile "
                 "the two and delete the one you do not want.",
                 "كان هناك استعلام بالاسم نفسه. ولا يستبدل الاستيراد شيئًا — راجع الاثنين "
                 "واحذف ما لا تريده.")],
          ],
          widths=[2.2, 4.4])

    h1(doc, T("16. Appendix B — Regenerating this manual",
              "١٦. الملحق ب — إعادة توليد هذا الدليل"))
    para(doc, T(
        "This document is generated, not hand-maintained. After a change to the application, "
        "rebuild it so the screenshots and text match what shipped.",
        "هذا المستند مولَّد آليًا وليس محرَّرًا يدويًا. وبعد أي تغيير في التطبيق أعد بناءه حتى "
        "تطابق الصور والنصوص ما جرى إصداره."))
    numbered(doc, [
        T("Start the API and the Angular client (see the project README).",
          "شغّل واجهة API وتطبيق Angular (انظر ملف README للمشروع)."),
        T("From docs/user-manual, run:  npm install   (first time only)",
          "من المجلد docs/user-manual نفّذ:  npm install   (في المرة الأولى فقط)"),
        T("Run:  npm run manual", "نفّذ:  npm run manual"),
    ])
    para(doc, T(
        "That recaptures every screenshot in both languages into docs/user-manual/screenshots "
        "and rewrites both documents. The text lives in build-docx.py; the screenshot list "
        "lives in capture-screenshots.js. Neither script modifies application data — the "
        "capture run blocks every write request at the network layer.",
        "يعيد ذلك التقاط كل الصور باللغتين في docs/user-manual/screenshots ويعيد كتابة "
        "المستندين. ويوجد النص في build-docx.py، وقائمة الصور في capture-screenshots.js. ولا "
        "يعدّل أيٌّ من البرنامجين بيانات التطبيق — إذ تحجب عملية الالتقاط كل طلبات الكتابة على "
        "مستوى الشبكة."))
    para(doc, T(
        "Open the finished document in Word, right-click the Contents table and choose Update "
        "Field to fill in the page numbers.",
        "افتح المستند النهائي في Word، وانقر بزر الفأرة الأيمن على جدول المحتويات واختر «تحديث "
        "الحقل» لتعبئة أرقام الصفحات."))


# --------------------------------------------------------------------------- entry point

def build(lang):
    global LANG
    LANG = lang
    _figure_no[0] = 0

    doc = new_document()
    add_page_footer(doc)
    cover(doc)
    toc(doc)
    build_manual(doc)
    if is_rtl():
        apply_rtl(doc)

    out = os.path.join(HERE, f"DotNetDBTasks-User-Manual-{lang.upper()}.docx")
    doc.save(out)

    shots = os.path.join(HERE, "screenshots", lang)
    have = len([f for f in os.listdir(shots) if f.endswith(".png")]) if os.path.isdir(shots) else 0
    print(f"{lang}: wrote {os.path.basename(out)} — "
          f"{_figure_no[0]} figures, {have} screenshots available")


def main():
    langs = sys.argv[1:] or ["en", "ar"]
    for lang in langs:
        build(lang)


if __name__ == "__main__":
    main()
