#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Builds the Jadwal user manual as a Word document, in English and Arabic.

    python build-docx.py            # both languages
    python build-docx.py en         # one language

Reads screenshots/<lang>/*.png (produced by capture-screenshots.js) and writes
Jadwal-User-Manual-EN.docx / -AR.docx. A screenshot that is missing is replaced by a
visible placeholder rather than failing the build, so a manual is always produced.

The text lives in `build_manual()` near the bottom. Every string is written once as
T("English", "العربية") and the active language picks one, so the two documents cannot drift
apart — add a section and you are forced to supply both.
"""

import copy
import os
import re
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
    from docx.text.run import Run
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

# The tick in yes/no tables. Neither body font has a check-mark glyph, so Word drew it as an
# empty box; Segoe UI Symbol ships with Windows and has one. Set on every font slot so neither
# Word's script detection nor the RTL pass swaps it back.
TICK = "\u2714"
TICK_FONT = "Segoe UI Symbol"
TICK_COLOR = RGBColor(0x05, 0x7A, 0x55)

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
    if rfonts is not None and rfonts.get(qn("w:ascii")) == TICK_FONT:
        return  # a tick: the Arabic font has no check-mark glyph either
    if rfonts is None:
        rfonts = OxmlElement("w:rFonts")
        rpr.insert(0, rfonts)
    rfonts.set(qn("w:cs"), ARABIC_FONT)
    rfonts.set(qn("w:ascii"), ARABIC_FONT)
    rfonts.set(qn("w:hAnsi"), ARABIC_FONT)


# English inside the Arabic manual. Marking a run right-to-left makes Word lay out its neutral
# characters — slashes, dots, brackets, hyphens — right-to-left too, so "/user/queries" printed as
# "user/queries/" and "today-N" as "N-today". Each stretch of English is therefore split into a
# run of its own, left without the RTL flag, and wrapped in a left-to-right embedding (LRE … PDF).
# Tested in Word 2013: left-to-right marks alone are not enough there — Word still resolves the
# neutrals right-to-left, and even reverses the word order of an address.
_ARABIC_LETTERS = "\u0600-\u06FF\u0750-\u077F\uFB50-\uFDFF\uFE70-\uFEFF"
_NON_ARABIC = re.compile(f"[^{_ARABIC_LETTERS}]+")
_LATIN = re.compile(r"[A-Za-z@{}/\\_]")
# Spacing and punctuation at the edge of an English stretch belong to the Arabic sentence around it.
_EDGE = " \t\u00a0«»“”\"'.,:;—–"
_LRE, _PDF = "\u202a", "\u202c"


def _bidi_pieces(text):
    """Splits text into (is_english, piece) pairs; English pieces have their edges trimmed."""
    pieces, pos = [], 0
    for m in _NON_ARABIC.finditer(text):
        seg = m.group(0)
        lead = len(seg) - len(seg.lstrip(_EDGE))
        core = seg.strip(_EDGE)
        if not _LATIN.search(core):
            continue
        start = m.start() + lead
        end = start + len(core)
        if start > pos:
            pieces.append((False, text[pos:start]))
        pieces.append((True, text[start:end]))
        pos = end
    if pos < len(text):
        pieces.append((False, text[pos:]))
    return pieces


def _mark_ltr_run(run):
    """English inside the Arabic document: same font as the text around it, no RTL flag.

    Word sizes and emboldens right-to-left text from the complex-script properties (szCs, bCs,
    iCs) and left-to-right text from the plain ones (sz, b, i). The Arabic around this run follows
    the first set, so the English is given the same values — otherwise it comes out a different
    size, or bold where the Arabic beside it is not.
    """
    rpr = run._r.get_or_add_rPr()
    rfonts = rpr.find(qn("w:rFonts"))
    if rfonts is None:
        rfonts = OxmlElement("w:rFonts")
        rpr.insert(0, rfonts)
    for slot in ("w:ascii", "w:hAnsi", "w:cs"):
        rfonts.set(qn(slot), ARABIC_FONT)
    for plain, complex_ in (("w:sz", "w:szCs"), ("w:b", "w:bCs"), ("w:i", "w:iCs")):
        for old in rpr.findall(qn(plain)):
            rpr.remove(old)
        source = rpr.find(qn(complex_))
        if source is not None:
            copy_ = OxmlElement(plain)
            for key, value in source.attrib.items():
                copy_.set(key, value)
            source.addprevious(copy_)


def _mark_rtl_paragraph(paragraph):
    ppr = paragraph._p.get_or_add_pPr()
    bidi = OxmlElement("w:bidi")
    bidi.set(qn("w:val"), "1")
    ppr.append(bidi)
    for run in list(paragraph.runs):
        pieces = _bidi_pieces(run.text) if run.text else []
        if not any(is_english for is_english, _ in pieces):
            _mark_rtl_run(run)
            continue
        template = copy.deepcopy(run._r)
        anchor = None
        for is_english, piece in pieces:
            if anchor is None:
                element = run._r
            else:
                element = copy.deepcopy(template)
                anchor.addnext(element)
            anchor = element
            part = Run(element, paragraph)
            if is_english:
                # The text's own direction marks would sit inside the embedding and nudge its
                # edges; the embedding does their job, so they go.
                part.text = _LRE + piece.replace("\u200e", "").replace("\u200f", "") + _PDF
                _mark_ltr_run(part)
            else:
                part.text = piece
                _mark_rtl_run(part)


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
    p.add_run(T("Jadwal User Manual  ·  page ",
                "دليل مستخدم «جدول»  ·  صفحة "))
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


def _tick_run(run):
    """Draws the run in the symbol font, on every slot, so the tick is a tick and not a box."""
    run.font.name = TICK_FONT
    run.font.bold = True
    run.font.color.rgb = TICK_COLOR
    rpr = run._r.get_or_add_rPr()
    rfonts = rpr.find(qn("w:rFonts"))
    if rfonts is None:
        rfonts = OxmlElement("w:rFonts")
        rpr.insert(0, rfonts)
    for slot in ("w:ascii", "w:hAnsi", "w:cs", "w:eastAsia"):
        rfonts.set(qn(slot), TICK_FONT)


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
            if text == TICK:
                _tick_run(run)
                p.alignment = WD_ALIGN_PARAGRAPH.CENTER
            elif text == "—" and i > 0:
                run.font.color.rgb = RGBColor(0x9C, 0xA3, 0xAF)
                p.alignment = WD_ALIGN_PARAGRAPH.CENTER
            elif i == 0 and len(header) > 2:
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

    centered(T("Jadwal", "جدول"), 40, ACCENT, bold=True)
    centered(T("User Manual", "دليل المستخدم"), 24, RGBColor(0x1F, 0x25, 0x37))
    centered(T("Features, screens and permissions",
               "الميزات والشاشات والصلاحيات"), 13, MUTED, italic=True)

    for _ in range(10):
        doc.add_paragraph()

    centered(T("A tool for running database queries safely: queries written in advance, "
               "controlled access,\na record of every run, and exports on a schedule.",
               "نظام لتشغيل استعلامات قواعد البيانات بأمان: استعلامات مكتوبة مسبقًا، وصلاحيات "
               "محددة،\nوسجل لكل تشغيل، وتصدير في أوقات محددة."), 11, MUTED)

    for _ in range(6):
        doc.add_paragraph()

    centered(T(f"Generated {date.today().strftime('%d %B %Y')}",
               f"تاريخ الإصدار {date.today().strftime('%Y-%m-%d')}"), 9.5, MUTED)
    page_break(doc)


def toc(doc):
    h1(doc, T("Contents", "المحتويات"))
    p = doc.add_paragraph()
    _field(p, r'TOC \o "1-3" \h \z \u')
    para(doc, T("Right-click the table above and choose “Update Field” to fill it in.",
                "انقر بزر الفأرة الأيمن على الجدول أعلاه واختر «تحديث الحقل» لملئه."),
         italic=True, size=9)
    page_break(doc)


# --------------------------------------------------------------------------- the manual

def build_manual(doc):

    # ===================================================================== 1
    h1(doc, T("1. About this manual", "١. عن هذا الدليل"))
    para(doc, T("This manual explains every screen in Jadwal, what each button does, and who "
                "is allowed to use it. It is written for four groups of people, and each "
                "chapter says at the top who it is for:",
                "يشرح هذا الدليل كل شاشة في نظام «جدول»، وما يفعله كل زر فيها، ومن يُسمح له "
                "باستخدامها. وهو مكتوب لأربع فئات من الناس، وفي بداية كل فصل تجد الفئة التي "
                "يخصّها:"))
    bullets(doc, [
        (T("Users — ", "المستخدمون — "),
         T("people who run the queries given to them and look at the results.",
           "من يشغّلون الاستعلامات المتاحة لهم ويطّلعون على نتائجها.")),
        (T("Administrators — ", "المسؤولون — "),
         T("people who write queries, organise them, decide who can use them, schedule "
           "exports and manage accounts.",
           "من يكتبون الاستعلامات وينظّمونها ويحدّدون من يستخدمها ويجدولون التصدير ويديرون "
           "الحسابات.")),
        (T("Auditors — ", "المدققون — "),
         T("people who review what was run and what was changed, without being able to run or "
           "change anything themselves.",
           "من يراجعون ما تم تشغيله وما تم تغييره، دون أن يستطيعوا تشغيل أو تغيير أي شيء "
           "بأنفسهم.")),
        (T("Access Managers — ", "مديرو الصلاحيات — "),
         T("people who decide who can use which queries, without seeing or running the SQL.",
           "من يحدّدون من يستطيع استخدام كل استعلام، دون أن يروا نص SQL أو يشغّلوه.")),
    ])
    para(doc, T("Chapters 2 to 4 are for everyone. Chapters 5, 7 and 8 are the everyday guide "
                "for Users — running queries and reports, and watching dashboards. Chapters 6 "
                "and 9 to 13 are for administrators, and so are the parts of 7 and 8 about "
                "building reports and dashboards. Chapter 14 explains the two logs that "
                "record what happened in the system.",
                "الفصول من ٢ إلى ٤ للجميع. والفصول ٥ و٧ و٨ هي الدليل اليومي للمستخدمين: تشغيل "
                "الاستعلامات والتقارير ومتابعة لوحات المعلومات. والفصل ٦ والفصول من ٩ إلى ١٣ "
                "للمسؤولين، وكذلك أجزاء الفصلين ٧ و٨ عن إنشاء التقارير ولوحات المعلومات. "
                "ويشرح الفصل ١٤ السجلّين اللذين يحفظان ما حدث في النظام."))
    note(doc, T("Every picture in this manual was taken from a working system, in the dark "
                "theme. If your screen looks different, your account may have different roles "
                "— Chapter 4 explains what each role can see.",
                "أُخذت كل الصور في هذا الدليل من نظام يعمل فعلًا، بالوضع الداكن. وإذا كانت "
                "الشاشة أمامك مختلفة، فربما لحسابك أدوار مختلفة — ويشرح الفصل ٤ ما يراه كل "
                "دور."))

    # ===================================================================== 2
    h1(doc, T("2. What the system does",
              "٢. ماذا يفعل النظام"))
    para(doc, T("Jadwal lets a few people write SQL queries once, and lets many more people "
                "run them safely — without direct access to the database and without being "
                "able to change the SQL. On top of that it controls who can use what, keeps a "
                "full record of what happened, and can run the same queries at set times and "
                "save the results to files.",
                "يسمح نظام «جدول» لعدد قليل من الأشخاص بكتابة استعلامات SQL مرة واحدة، ثم يسمح "
                "لعدد أكبر بكثير بتشغيلها بأمان — دون دخول مباشر إلى قاعدة البيانات ودون "
                "إمكانية تغيير نص SQL. ويضاف إلى ذلك تحديد من يستخدم ماذا، وسجل كامل لكل ما "
                "حدث، وتشغيل الاستعلامات نفسها في أوقات محددة وحفظ النتائج في ملفات."))

    h2(doc, T("2.1 The core idea", "٢.١ الفكرة الأساسية"))
    numbered(doc, [
        T("An administrator writes a query and sets up its parameters — for example a start "
          "date, an end date and a branch.",
          "يكتب المسؤول استعلامًا ويحدّد معايير البحث فيه — مثل تاريخ البداية وتاريخ النهاية "
          "والفرع."),
        T("The administrator decides who can run it: by role, by user group, by person, or by "
          "putting it in a query group and giving access to the group.",
          "يحدّد المسؤول من يستطيع تشغيله: حسب الدور، أو مجموعة المستخدمين، أو شخص محدد، أو "
          "بوضعه في مجموعة استعلامات ثم إعطاء المجموعة لمن يحتاجها."),
        T("A user opens the query, fills in a ready-made form and runs it. They never see or "
          "change the SQL.",
          "يفتح المستخدم الاستعلام ويملأ نموذجًا جاهزًا ثم يشغّله، ولا يرى نص SQL ولا يستطيع "
          "تغييره."),
        T("The result is shown page by page, can be sorted and filtered, and can be exported "
          "to Excel, CSV, PDF, Word or JSON.",
          "تظهر النتيجة مقسّمة إلى صفحات، ويمكن ترتيبها وتصفيتها، ويمكن تصديرها إلى Excel أو "
          "CSV أو PDF أو Word أو JSON."),
        T("Every run is recorded in the execution log, and every change an administrator "
          "makes is recorded in the system audit.",
          "يُسجَّل كل تشغيل في سجل التنفيذ، ويُسجَّل كل تغيير يقوم به المسؤول في سجل عمليات "
          "النظام."),
    ])

    h2(doc, T("2.2 Key terms", "٢.٢ المصطلحات الأساسية"))
    table(doc,
          [T("Term", "المصطلح"), T("Meaning", "المعنى")],
          [
              [T("Dynamic query", "استعلام ديناميكي"),
               T("A saved SQL statement with a name and parameters. It may read data (SELECT) "
                 "or change it (INSERT / UPDATE / DELETE).",
                 "جملة SQL محفوظة لها اسم ومعايير بحث. وقد تقرأ البيانات (SELECT) أو تغيّرها "
                 "(INSERT / UPDATE / DELETE).")],
              [T("Parameter", "معيار بحث"),
               T("A value the user fills in before running a query. There are five types: "
                 "String (text), Number, Date, Boolean (yes/no) and Dropdown (a list to pick "
                 "from).",
                 "قيمة يملؤها المستخدم قبل تشغيل الاستعلام. ولها خمسة أنواع: نص، ورقم، "
                 "وتاريخ، ونعم/لا، وقائمة منسدلة يُختار منها.")],
              [T("Query group", "مجموعة استعلامات"),
               T("A folder of queries. Giving someone access to the group gives them every "
                 "query in it. A query can be in more than one group.",
                 "مجلد فيه استعلامات. وإعطاء المجموعة لشخص يعطيه كل الاستعلامات التي فيها. "
                 "ويمكن أن يكون الاستعلام في أكثر من مجموعة.")],
              [T("User group", "مجموعة مستخدمين"),
               T("A named list of people, kept in this system. Giving a query to a user group "
                 "gives it to everyone in the group. It is not an Active Directory group, and "
                 "it is not a query group.",
                 "قائمة أشخاص لها اسم، محفوظة داخل هذا النظام. وإعطاء استعلام لمجموعة "
                 "مستخدمين يعطيه لكل من فيها. وهي ليست مجموعة في Active Directory، وليست "
                 "مجموعة استعلامات.")],
              [T("Database connection", "اتصال قاعدة بيانات"),
               T("A saved database login (“DB User”) that a query runs through. Passwords are "
                 "stored encrypted.",
                 "اسم مستخدم وكلمة مرور لقاعدة البيانات محفوظان باسم («مستخدم قاعدة "
                 "البيانات»)، ويعمل الاستعلام من خلالهما. وكلمات المرور محفوظة مشفّرة.")],
              [T("Execution", "تنفيذ"),
               T("One run of a query by one person, recorded in the execution log.",
                 "مرة واحدة شغّل فيها شخصٌ استعلامًا، وتُسجَّل في سجل التنفيذ.")],
              [T("Scheduled task", "مهمة مجدولة"),
               T("One or more queries that run by themselves at set times, saving the results "
                 "to files on the server.",
                 "استعلام أو أكثر يعمل تلقائيًا في أوقات محددة، وتُحفظ النتائج في ملفات على "
                 "الخادم.")],
              [T("Dashboard", "لوحة معلومات"),
               T("A page of tiles — numbers, charts and short tables — each fed by a saved "
                 "query and updating itself every so often.",
                 "صفحة فيها عناصر — أرقام ورسوم بيانية وجداول قصيرة — يعمل كل عنصر من استعلام "
                 "محفوظ ويتحدّث تلقائيًا كل فترة.")],
              [T("Role", "دور"),
               T("A named set of permissions given to an account. An account can have more "
                 "than one role, and then it has all of their permissions together.",
                 "اسم لمجموعة صلاحيات تُعطى للحساب. ويمكن أن يكون للحساب أكثر من دور، فتكون "
                 "له صلاحياتها كلها معًا.")],
          ],
          widths=[1.6, 5.0])

    h2(doc, T("2.3 How queries are kept safe",
              "٢.٣ كيف تبقى الاستعلامات آمنة"))
    bullets(doc, [
        (T("Values are never pasted into the SQL. ",
           "لا تُلصق القيم داخل نص SQL أبدًا. "),
         T("Each value is sent to the database separately from the SQL, so no value can "
           "change what the query does.",
           "تُرسَل كل قيمة إلى قاعدة البيانات منفصلةً عن نص الاستعلام، فلا يمكن لأي قيمة أن "
           "تغيّر ما يفعله الاستعلام.")),
        (T("Queries that change data show a preview first. ",
           "الاستعلامات التي تغيّر البيانات تعرض معاينة أولًا. "),
         T("An INSERT, UPDATE or DELETE is first tried and then undone, so you see how many "
           "rows would change — and, for UPDATE and DELETE, which rows — before anything is "
           "saved.",
           "يُجرَّب INSERT أو UPDATE أو DELETE أولًا ثم يُلغى، فترى عدد الصفوف التي ستتغيّر — "
           "والصفوف نفسها في UPDATE وDELETE — قبل أن يُحفظ أي تغيير.")),
        (T("Old values are kept. ",
           "تُحفظ القيم القديمة. "),
         T("For UPDATE and DELETE, the rows as they were before the change are saved with the "
           "execution log.",
           "في UPDATE وDELETE تُحفظ الصفوف كما كانت قبل التغيير مع سجل التنفيذ.")),
        (T("Every query has a time limit. ",
           "لكل استعلام وقت أقصى. "),
         T("Each query has its own time limit, and slow queries can be marked long-running so "
           "the connection does not cut them off.",
           "لكل استعلام وقت أقصى للتشغيل، ويمكن جعل الاستعلامات البطيئة «طويلة التنفيذ» حتى "
           "لا ينقطع الاتصال قبل انتهائها.")),
        (T("Database passwords are encrypted. ",
           "كلمات مرور قواعد البيانات مشفّرة. "),
         T("Database passwords are stored encrypted (AES-256) and are never written to an "
           "export file.",
           "تُحفظ كلمات مرور قواعد البيانات مشفّرة (AES-256)، ولا تُكتب أبدًا في أي ملف "
           "تصدير.")),
    ])

    page_break(doc)

    # ===================================================================== 3
    h1(doc, T("3. Getting started", "٣. البدء"))

    h2(doc, T("3.1 Signing in", "٣.١ تسجيل الدخول"))
    para(doc, T("Open the system's address in a browser and sign in with your username and "
                "password. Some accounts are created in the system and some come from Active "
                "Directory — either way you sign in on the same form.",
                "افتح عنوان النظام في المتصفح، وادخل باسم المستخدم وكلمة المرور. بعض الحسابات "
                "أُنشئت داخل النظام وبعضها جاء من Active Directory، والدخول في الحالتين من "
                "النموذج نفسه."))
    para(doc, T("If the administrator has turned on automatic Windows sign-in, you do not "
                "need any of that: on a company computer you are signed in as the page opens, "
                "and you never see the form. If that cannot happen — you are on a computer "
                "outside the company network, or you have just signed out — the form appears "
                "and you sign in yourself.",
                "إذا فعّل المسؤول الدخول التلقائي من Windows فلن تحتاج إلى ذلك: على أجهزة "
                "الشركة تدخل تلقائيًا عند فتح الصفحة دون أن ترى النموذج. وإذا لم يحدث ذلك — "
                "كأن تكون على جهاز خارج شبكة الشركة أو سجّلت الخروج للتو — يظهر النموذج وتدخل "
                "بنفسك."))
    figure(doc, "01-login",
           T("The sign-in page. The two buttons in the top corner change the language and the "
             "colours before you sign in.",
             "صفحة تسجيل الدخول. الزرّان في الزاوية العليا يغيّران اللغة والألوان قبل الدخول."))
    bullets(doc, [
        (T("Show / hide password — ", "إظهار/إخفاء كلمة المرور — "),
         T("the eye icon inside the password box.", "أيقونة العين داخل حقل كلمة المرور.")),
        (T("Language — ", "اللغة — "),
         T("the translate icon in the top corner switches between English and Arabic, even "
           "before you sign in.",
           "أيقونة الترجمة في الزاوية العليا تغيّر اللغة بين العربية والإنجليزية، حتى قبل "
           "الدخول.")),
        (T("Theme — ", "النمط — "),
         T("the sun/moon icon switches between the light and dark themes.",
           "أيقونة الشمس/القمر تبدّل بين الوضعين الفاتح والداكن.")),
    ])
    para(doc, T("After signing in you land on the first page you are allowed to open, in this "
                "order: My Queries if you can run queries; then Dashboards if you can view "
                "them; then Manage Queries, the execution logs, the system audit, Users, User "
                "Groups, Scheduled Tasks, and Settings. If none of those is open to you, you "
                "land on My Schedules.",
                "بعد الدخول تُفتح أول صفحة مسموح لك بها، بهذا الترتيب: «الاستعلامات» إن كان "
                "مسموحًا لك تشغيل الاستعلامات، ثم «لوحات المعلومات» إن كان مسموحًا لك عرضها، "
                "ثم «إدارة الاستعلامات»، فسجلات التنفيذ، فسجل عمليات النظام، فالمستخدمون، "
                "فمجموعات المستخدمين، فالمهام المجدولة، فالإعدادات. فإن لم يكن أيٌّ منها "
                "مسموحًا لك تُفتح «مهامي المجدولة»."))
    para(doc, T("So an administrator usually starts on My Queries (the Admin role can run "
                "queries like anyone else), an Access Manager on Manage Queries, an Auditor "
                "on the execution logs, and a User on My Queries.",
                "فعادةً يبدأ المسؤول في «الاستعلامات» (لأن دور Admin يستطيع تشغيل الاستعلامات "
                "مثل غيره)، ويبدأ مدير الصلاحيات في «إدارة الاستعلامات»، والمدقق في سجلات "
                "التنفيذ، والمستخدم في «الاستعلامات»."))

    h2(doc, T("3.2 The main window",
              "٣.٢ الشاشة الرئيسية"))
    para(doc, T("The bar across the top is how you move around. On one side is the site logo "
                "(or its name if there is no logo); on the other are the pages you are "
                "allowed to open, then the account menu.",
                "الشريط العلوي هو طريقك للتنقل. في أحد طرفيه شعار الموقع (أو اسمه إن لم يُرفع "
                "شعار)، وفي الطرف الآخر الصفحات المسموح لك بها، ثم قائمة الحساب."))
    figure(doc, "02-admin-landing",
           T("The application as an administrator sees it: My Queries, History, and the "
             "Queries, Users & Access and Audit menus, with the account menu at the end.",
             "التطبيق كما يراه المسؤول: «الاستعلامات» و«السجل» وقوائم «الاستعلامات» "
             "و«المستخدمون والصلاحيات» و«التدقيق»، وقائمة الحساب في الطرف."))
    para(doc, T("The pages are grouped into three drop-down menus, plus your own pages, which "
                "are always shown:",
                "الصفحات مرتّبة في ثلاث قوائم منسدلة، إضافةً إلى صفحاتك الشخصية الظاهرة "
                "دائمًا:"))
    table(doc,
          [T("Menu", "القائمة"), T("Contains", "تحتوي على"), T("Visible to", "تظهر لـ")],
          [
              [T("My Queries", "الاستعلامات"),
               T("The queries assigned to you", "الاستعلامات المخصَّصة لك"),
               T("Everyone", "الجميع")],
              [T("Dashboards", "لوحات المعلومات"),
               T("The dashboards shared with you", "لوحات المعلومات المشارَكة معك"),
               T("Anyone holding View dashboards (Chapter 8)",
                 "كل من يحمل صلاحية «عرض لوحات المعلومات» (الفصل ٨)")],
              [T("History", "السجل"),
               T("Your own execution history", "سجل تنفيذك الشخصي"),
               T("Everyone", "الجميع")],
              [T("Schedules", "المهام المجدولة"),
               T("Status of scheduled tasks you were added to",
                 "حالة المهام المجدولة التي أُضفت إليها"),
               T("Users (not shown to Admin and Auditor, who use the full Scheduled Tasks "
                 "page)",
                 "المستخدمون (لا تظهر للمسؤول والمدقق لأنهما يستخدمان صفحة المهام الكاملة)")],
              [T("Queries", "الاستعلامات"),
               T("Manage Queries, Query Groups, Reports, Dashboards, Schedules",
                 "إدارة الاستعلامات، ومجموعات الاستعلامات، والتقارير، ولوحات المعلومات، والمهام "
                 "المجدولة"),
               T("Admin, Access Manager, Auditor (each sees only its own entries)",
                 "المسؤول ومدير الصلاحيات والمدقق (يرى كلٌّ منهم ما يخصّه فقط)")],
              [T("Users & Access", "المستخدمون والصلاحيات"),
               T("Users, User Groups, AD Users, DB Users",
                 "المستخدمون، ومجموعات المستخدمين، ومستخدمو Active Directory، ومستخدمو قواعد "
                 "البيانات"),
               T("Admin, Access Manager", "المسؤول ومدير الصلاحيات")],
              [T("Audit", "التدقيق"),
               T("Logs, System Audit", "سجلات التنفيذ، وسجل عمليات النظام"),
               T("Admin, Auditor", "المسؤول والمدقق")],
              [T("Account", "الحساب"),
               T("Language, theme, Settings, Website branding, Logout",
                 "اللغة، والنمط، والإعدادات، وهوية الموقع، وتسجيل الخروج"),
               T("Everyone", "الجميع")],
          ],
          widths=[1.4, 2.9, 2.3])
    figure(doc, "04-nav-queries",
           T("The Queries menu. An Access Manager sees only the first two entries; an Auditor "
             "sees only Schedules.",
             "قائمة «الاستعلامات». يرى مدير الصلاحيات أول عنصرين فقط، ويرى المدقق «المهام "
             "المجدولة» وحدها."))
    figure(doc, "05-nav-people",
           T("The Users & Access menu. An Access Manager sees Users and User Groups; AD Users "
             "and DB Users are for administrators only. AD Users also disappears when Active "
             "Directory is turned off in Settings.",
             "قائمة «المستخدمون والصلاحيات». يرى مدير الصلاحيات «المستخدمون» و«مجموعات "
             "المستخدمين»، أما «مستخدمو Active Directory» و«مستخدمو قواعد البيانات» فللمسؤول "
             "فقط. وتختفي «مستخدمو Active Directory» إذا أُوقفت ميزة Active Directory من "
             "«الإعدادات»."))
    figure(doc, "06-nav-audit",
           T("The Audit menu — the two logs, which can only be read, for Admin and Auditor.",
             "قائمة «التدقيق» — السجلّان، وهما للقراءة فقط، للمسؤول والمدقق."))

    h2(doc, T("3.3 The account menu", "٣.٣ قائمة الحساب"))
    para(doc, T("The round icon at the end of the bar opens the account menu. It shows who "
                "you are signed in as and holds your personal settings.",
                "الأيقونة الدائرية في طرف الشريط تفتح قائمة الحساب، وفيها اسمك وإعداداتك "
                "الشخصية."))
    figure(doc, "03-account-menu",
           T("The account menu as an administrator sees it. Settings and Website branding appear "
             "only for administrators.",
             "قائمة الحساب كما يراها المسؤول. لا يظهر عنصرا «الإعدادات» و«هوية الموقع» إلا "
             "للمسؤولين."))
    bullets(doc, [
        (T("Switch to العربية / English — ", "التبديل إلى English / العربية — "),
         T("changes the language of the screens. The page reloads.",
           "يغيّر لغة الشاشات، وتُفتح الصفحة من جديد.")),
        (T("Switch to dark / light mode — ", "التبديل إلى الوضع الداكن/الفاتح — "),
         T("changes the colours straight away; the menu stays open so you can compare.",
           "يغيّر الألوان فورًا، وتبقى القائمة مفتوحة لتقارن.")),
        (T("Settings — ", "الإعدادات — "),
         T("settings for the whole system (administrators only, see Chapter 16).",
           "إعدادات تخص النظام كله (للمسؤولين فقط، انظر الفصل ١٦).")),
        (T("Website branding — ", "هوية الموقع — "),
         T("change the logo, the site name and the browser tab icon (administrators only, see "
           "Chapter 15).",
           "تغيير الشعار واسم الموقع وأيقونة تبويب المتصفح (للمسؤولين فقط، انظر الفصل ١٥).")),
        (T("Logout — ", "تسجيل الخروج — "),
         T("signs out and returns to the sign-in page.",
           "يخرجك من النظام ويعيدك إلى صفحة الدخول.")),
    ])

    h2(doc, T("3.4 Language and reading direction", "٣.٤ اللغة واتجاه القراءة"))
    para(doc, T("The screens come in English and Arabic. Changing the language flips the "
                "whole page between left-to-right and right-to-left, including menus, tables "
                "and windows. Your browser remembers your choice.",
                "الشاشات متوفرة بالعربية والإنجليزية. وتغيير اللغة يقلب الصفحة كلها بين "
                "اليمين واليسار، بما فيها القوائم والجداول والنوافذ. ويتذكر المتصفح اختيارك."))
    figure(doc, "81-alternate-language",
           T("The same page in the other language. Everything moves to the opposite side: the "
             "menus, the table columns and the account menu.",
             "الصفحة نفسها باللغة الأخرى. ينتقل كل شيء إلى الجهة المقابلة: القوائم وأعمدة "
             "الجداول وقائمة الحساب."))
    note(doc, T("Data always reads in its own direction, whatever the screen language — an "
                "Arabic query name reads correctly in the English screens, and the other way "
                "round. SQL and file paths always stay left-to-right.",
                "تظهر البيانات باتجاهها الصحيح مهما كانت لغة الشاشات — فاسم الاستعلام العربي "
                "يُقرأ صحيحًا في الشاشات الإنجليزية والعكس. أما نص SQL ومسارات الملفات فتبقى "
                "دائمًا من اليسار إلى اليمين."))

    h2(doc, T("3.5 Light and dark themes", "٣.٥ الوضعان الفاتح والداكن"))
    para(doc, T("The pictures in this manual use the dark theme. The light theme has exactly "
                "the same pages and buttons.",
                "صور هذا الدليل بالوضع الداكن، والوضع الفاتح فيه الصفحات والأزرار نفسها "
                "تمامًا."))
    figure(doc, "80-alternate-theme",
           T("The light theme. Every page, window and message works in both; the choice "
             "belongs to the browser, not the account.",
             "الوضع الفاتح. كل الصفحات والنوافذ والرسائل تعمل بالوضعين، والاختيار يخص المتصفح "
             "وليس الحساب."))

    page_break(doc)

    # ===================================================================== 4
    h1(doc, T("4. Roles and permissions", "٤. الأدوار والصلاحيات"))
    para(doc, T("This chapter explains exactly who can do what. The server checks permissions "
                "on every request — the screens hide what you cannot use, but that is only "
                "for convenience; the server is what actually stops it.",
                "يشرح هذا الفصل بالضبط من يستطيع فعل ماذا. ويتحقق الخادم من الصلاحيات في كل "
                "طلب — أما إخفاء ما لا تستطيع استخدامه من الشاشة فهو للتسهيل فقط، والخادم هو "
                "الذي يمنع فعلًا."))

    h2(doc, T("4.1 The roles that come with the system",
              "٤.١ الأدوار الجاهزة في النظام"))
    para(doc, T("A role is a named set of permissions. Four roles come with the system; an "
                "administrator can add more, and can change what any of them can do on "
                "Settings → Permissions (Chapter 16). Below is what each role can do when the "
                "system is first installed — not what it must always be.",
                "الدور اسم لمجموعة صلاحيات. ويأتي النظام بأربعة أدوار، ويستطيع المسؤول إضافة "
                "غيرها وتغيير ما يستطيعه أي منها من «الإعدادات ← الصلاحيات» (الفصل ١٦). وما "
                "يلي هو ما يستطيعه كل دور عند تثبيت النظام لأول مرة، وليس ما يجب أن يبقى عليه "
                "دائمًا."))
    para(doc, T("One account can have several roles, and then it has all of their "
                "permissions.",
                "ويمكن أن يكون للحساب الواحد أكثر من دور، فتكون له صلاحياتها كلها."))
    table(doc,
          [T("Role", "الدور"), T("Purpose", "الغرض")],
          [
              ["Admin",
               T("Full control: writes queries, gives access, schedules tasks, manages "
                 "accounts and connections, and reads both logs.",
                 "تحكم كامل: يكتب الاستعلامات، ويعطي الصلاحيات، ويجدول المهام، ويدير الحسابات "
                 "والاتصالات، ويقرأ السجلّين.")],
              ["User",
               T("Runs the queries given to them and sees their own history. The everyday "
                 "role.",
                 "يشغّل الاستعلامات المتاحة له ويرى سجله الشخصي. وهو دور الاستخدام اليومي.")],
              ["Auditor",
               T("Reads the execution logs, the old values saved before changes, the system "
                 "audit and the scheduled-task history. Cannot change anything and cannot run "
                 "queries.",
                 "يقرأ سجلات التنفيذ، والقيم القديمة المحفوظة قبل التغيير، وسجل عمليات "
                 "النظام، وسجل المهام المجدولة. ولا يستطيع تغيير أي شيء ولا تشغيل "
                 "الاستعلامات.")],
              ["AccessManager",
               T("Decides who can use which queries, and manages user accounts and user "
                 "groups. Cannot read a query's SQL, change a query or run one.",
                 "يحدّد من يستطيع استخدام كل استعلام، ويدير حسابات المستخدمين ومجموعاتهم. ولا "
                 "يستطيع قراءة نص SQL ولا تعديل الاستعلام ولا تشغيله.")],
          ],
          widths=[1.5, 5.1])

    h2(doc, T("4.2 The list of permissions",
              "٤.٢ قائمة الصلاحيات"))
    para(doc, T("Every permission in the system, and which roles have it when the system is "
                "first installed (✔ means the role has it; a dash means it does not). These "
                "are only the starting values: you can change them on Settings → Permissions "
                "— except the Admin column, which cannot be changed.",
                "كل صلاحيات النظام، والأدوار التي تملكها عند تثبيت النظام لأول مرة (علامة ✔ "
                "تعني أن الدور يملكها، والشرطة تعني أنه لا يملكها). وهذه قيم البداية فقط: "
                "يمكنك تغييرها من «الإعدادات ← الصلاحيات»، ما عدا عمود Admin فلا يمكن تغييره."))
    yes, dash = TICK, "—"
    table(doc,
          [T("Permission",
             "الصلاحية"), "Admin", "Auditor", "AccessManager", "User"],
          [
              [T("See the query list",
                 "عرض قائمة الاستعلامات"),
               yes, dash, yes, dash],
              [T("Read a query's SQL",
                 "قراءة نص SQL للاستعلام"),
               yes, dash, dash, dash],
              [T("Create and edit queries",
                 "إنشاء الاستعلامات وتعديلها"),
               yes, dash, dash, dash],
              [T("Export and import query definitions",
                 "تصدير تعريفات الاستعلامات واستيرادها"),
               yes, dash, dash, dash],
              [T("Run queries",
                 "تنفيذ الاستعلامات"),
               yes, dash, dash, yes],
              [T("Grant an individual query",
                 "منح استعلام مفرد"),
               yes, dash, dash, dash],
              [T("Grant a query group",
                 "منح مجموعة استعلامات"),
               yes, dash, yes, dash],
              [T("Create and edit query groups",
                 "إنشاء مجموعات الاستعلامات وتعديلها"),
               yes, dash, dash, dash],
              [T("See the report list",
                 "عرض قائمة التقارير"),
               yes, dash, dash, dash],
              [T("Create and edit reports",
                 "إنشاء التقارير وتعديلها"),
               yes, dash, dash, dash],
              [T("Run reports",
                 "تشغيل التقارير"),
               yes, dash, dash, dash],
              [T("Grant a report",
                 "منح صلاحية تقرير"),
               yes, dash, dash, dash],
              [T("Export and import report definitions",
                 "تصدير تعريفات التقارير واستيرادها"),
               yes, dash, dash, dash],
              [T("See the dashboard list",
                 "عرض قائمة لوحات المعلومات"),
               yes, dash, dash, dash],
              [T("Create and edit dashboards",
                 "إنشاء لوحات المعلومات وتعديلها"),
               yes, dash, dash, dash],
              [T("View dashboards",
                 "عرض لوحات المعلومات"),
               yes, dash, dash, dash],
              [T("Grant a dashboard",
                 "منح صلاحية لوحة معلومات"),
               yes, dash, dash, dash],
              [T("Export and import dashboard definitions",
                 "تصدير تعريفات لوحات المعلومات واستيرادها"),
               yes, dash, dash, dash],
              [T("See the user list",
                 "عرض قائمة المستخدمين"),
               yes, dash, yes, dash],
              [T("Create and edit users",
                 "إنشاء المستخدمين وتعديلهم"),
               yes, dash, yes, dash],
              [T("See user groups",
                 "عرض مجموعات المستخدمين"),
               yes, dash, yes, dash],
              [T("Create user groups and set membership",
                 "إنشاء مجموعات المستخدمين وتحديد أعضائها"),
               yes, dash, yes, dash],
              [T("See directory data",
                 "عرض بيانات الدليل"),
               yes, dash, yes, dash],
              [T("Search and import from Active Directory",
                 "البحث والاستيراد من Active Directory"),
               yes, dash, dash, dash],
              [T("Manage database connections",
                 "إدارة اتصالات قواعد البيانات"),
               yes, dash, dash, dash],
              [T("See every scheduled task",
                 "عرض جميع المهام المجدولة"),
               yes, yes, dash, dash],
              [T("Create and run scheduled tasks",
                 "إنشاء المهام المجدولة وتشغيلها"),
               yes, dash, dash, dash],
              [T("Download scheduled task files",
                 "تنزيل ملفات المهام المجدولة"),
               yes, dash, dash, dash],
              [T("Read execution logs",
                 "قراءة سجلات التنفيذ"),
               yes, yes, dash, dash],
              [T("Read the system audit trail",
                 "قراءة سجل عمليات النظام"),
               yes, yes, dash, dash],
              [T("Change the site logo, name and tab icon",
                 "تغيير شعار الموقع واسمه وأيقونة تبويبه"),
               yes, dash, dash, dash],
              [T("Change system settings",
                 "تغيير إعدادات النظام"),
               yes, dash, dash, dash],
              [T("Manage roles and permissions",
                 "إدارة الأدوار والصلاحيات"),
               yes, dash, dash, dash],
          ],
          widths=[2.9, 0.75, 0.8, 1.3, 1.0],
          font_size=8.5)
    para(doc, T("Three of these are worth knowing well. “See the query list” and “Read a "
                "query's SQL” are separate, so a role can manage access without reading the "
                "queries. “Run queries” is what makes My Queries and History appear in the "
                "top bar. And “Manage roles and permissions” is what changes this table — "
                "give it to very few people.",
                "ثلاث منها مهمة. «عرض قائمة الاستعلامات» و«قراءة نص SQL» صلاحيتان منفصلتان، "
                "فيستطيع دور أن يدير الصلاحيات دون أن يقرأ الاستعلامات. و«تنفيذ الاستعلامات» "
                "هي التي تُظهر «الاستعلامات» و«السجل» في الشريط العلوي. و«إدارة الأدوار "
                "والصلاحيات» هي التي تغيّر هذا الجدول — فأعطها لعدد قليل جدًا من الناس."))

    h2(doc, T("4.3 Rules that always apply",
              "٤.٣ قواعد ثابتة دائمًا"))

    h3(doc, T("Admin can always do everything",
              "دور Admin يستطيع كل شيء دائمًا"))
    para(doc, T("The Admin column on the Permissions tab is shown but locked, and the role "
                "cannot be deleted. This makes sure there is always someone who can open that "
                "page and fix mistakes. The server lets Admin through without even looking at "
                "the table, so even a badly saved table can be put right.",
                "عمود Admin في تبويب الصلاحيات ظاهر لكنه مقفل، ولا يمكن حذف هذا الدور. وهذا "
                "يضمن أن يبقى دائمًا من يستطيع فتح تلك الصفحة وإصلاح أي خطأ. فالخادم يسمح لـ "
                "Admin دون أن ينظر في الجدول أصلًا، فحتى لو حُفظ الجدول بشكل خاطئ يبقى من "
                "يستطيع إصلاحه."))

    h3(doc, T("A role that cannot run queries gives no queries",
              "الدور الذي لا يستطيع التشغيل لا يعطي أي استعلام"))
    para(doc, T("Every role appears in the lists where you give access, including roles you "
                "create. If you give a query to a role that does not have “Run queries”, "
                "nobody in that role can run it, because the server ignores such roles. The "
                "save is not refused — it simply does nothing. Give the role that permission "
                "first, or give the query another way.",
                "يظهر كل دور في قوائم إعطاء الصلاحيات، بما فيها الأدوار التي تنشئها. وإذا "
                "أعطيت استعلامًا لدور لا يملك «تنفيذ الاستعلامات» فلن يستطيع أحد في هذا الدور "
                "تشغيله، لأن الخادم يتجاهل هذه الأدوار. ولا يُرفض الحفظ — لكنه ببساطة لا يفعل "
                "شيئًا. فأعطِ الدور تلك الصلاحية أولًا، أو أعطِ الاستعلام بطريقة أخرى."))
    note(doc, T("Auditor and Access Manager do not have “Run queries”, and that is why those "
                "roles exist — but it is only the starting setting. An administrator can give "
                "it to either of them on purpose, and that is recorded in the audit log.",
                "لا يملك Auditor ولا AccessManager صلاحية «تنفيذ الاستعلامات»، ولهذا السبب "
                "وُجد هذان الدوران — لكنه إعداد البداية فقط. ويستطيع المسؤول إعطاءها لأيٍّ "
                "منهما عن قصد، ويُسجَّل ذلك في سجل عمليات النظام."),
        kind="warning")

    h3(doc, T("Seeing a query and reading its SQL are different",
              "رؤية الاستعلام شيء، وقراءة نص SQL شيء آخر"))
    para(doc, T("“See the query list” opens the list and the access pages; “Read a query's "
                "SQL” is what shows the SQL itself. A role with the first but not the second "
                "— which is how Access Manager is set up — can decide who uses a query "
                "without ever reading it. Export files contain the SQL, so exporting needs "
                "the export permission, not just the list one.",
                "صلاحية «عرض قائمة الاستعلامات» تفتح القائمة وصفحات الصلاحيات، أما «قراءة نص "
                "SQL» فهي التي تُظهر النص نفسه. والدور الذي يملك الأولى دون الثانية — وهكذا "
                "جُهّز AccessManager — يستطيع تحديد من يستخدم الاستعلام دون أن يقرأه أبدًا. "
                "وملفات التصدير تحتوي نص SQL، لذلك يحتاج التصدير إلى صلاحية التصدير، وليس "
                "صلاحية العرض فقط."))

    h3(doc, T("Nobody can give themselves permissions",
              "لا أحد يعطي نفسه صلاحيات"))
    para(doc, T("Someone who is not an administrator cannot change an account that has the "
                "Admin role, cannot give that role, cannot change their own roles, and cannot "
                "add themselves to a user group. The last two matter most: without them, "
                "anyone who manages permissions could give themselves something and use it "
                "straight away.",
                "من ليس مسؤولًا لا يستطيع تعديل حساب له دور Admin، ولا إعطاء هذا الدور، ولا "
                "تغيير أدواره هو، ولا إضافة نفسه إلى مجموعة مستخدمين. والأخيران هما الأهم: "
                "فبدونهما يستطيع أي شخص يدير الصلاحيات أن يعطي نفسه صلاحية ثم يستخدمها فورًا."))
    note(doc, T("Someone who can create users can still create a second account and sign in "
                "with it. That cannot be avoided when one role both creates users and "
                "controls access — but unlike giving yourself a permission, it leaves a new "
                "account and an audit entry behind.",
                "يبقى بإمكان من ينشئ المستخدمين أن ينشئ حسابًا آخر ويدخل به. ولا يمكن منع ذلك "
                "ما دام دور واحد ينشئ المستخدمين ويتحكم في الصلاحيات — لكنه، بخلاف إعطاء "
                "النفس صلاحية، يترك وراءه حسابًا جديدًا وسطرًا في سجل عمليات النظام."),
        kind="warning")

    h3(doc, T("Changes work straight away",
              "تعمل التغييرات فورًا"))
    para(doc, T("Permissions are checked on every request, not only when you sign in. Taking "
                "one away stops the very next click; nobody needs to sign out and in again.",
                "يتحقق النظام من الصلاحيات في كل طلب، وليس عند الدخول فقط. فسحب صلاحية يمنع "
                "النقرة التالية مباشرة، دون حاجة إلى الخروج والدخول من جديد."))

    h2(doc, T("4.4 What each role sees", "٤.٤ ما يراه كل دور"))
    para(doc, T("The top bar is built from your roles, so the quickest way to see what an "
                "account can do is to look at its top bar. Compare the administrator's bar in "
                "Figure 2 with the Auditor's below.",
                "يُبنى الشريط العلوي حسب أدوارك، لذا فأسرع طريقة لمعرفة ما يستطيعه الحساب هي "
                "النظر إلى شريطه العلوي. قارن شريط المسؤول في الشكل ٢ بشريط المدقق أدناه."))
    figure(doc, "90-auditor-landing",
           T("An Auditor starts on the execution logs. The bar has only Queries (with the "
             "scheduled tasks, read-only) and Audit. My Queries and History are not there: "
             "this role can never be given a query, so both pages would always be empty.",
             "يبدأ المدقق بعد الدخول في سجلات التنفيذ. ولا يوجد في الشريط سوى قائمة "
             "«الاستعلامات» (وفيها المهام المجدولة للاطلاع فقط) و«التدقيق». ولا تظهر صفحتا "
             "«الاستعلامات» و«السجل»، لأن هذا الدور لا يمكن أن يُعطى أي استعلام، فتبقى "
             "الصفحتان فارغتين دائمًا."))
    figure(doc, "91-auditor-account-menu",
           T("The Auditor's account menu: language, theme and logout only — no Settings and "
             "no Website branding.",
             "قائمة حساب المدقق: اللغة والألوان وتسجيل الخروج فقط — دون «الإعدادات» و«هوية "
             "الموقع»."))
    note(doc, T("The same goes for an Access Manager. Typing the address of one of those "
                "pages does not help — the system sends you back to a page your role can "
                "open. An account that also has a role that does give queries keeps both "
                "pages.",
                "وينطبق الأمر نفسه على مدير الصلاحيات. ولا تفيد كتابة عنوان إحدى الصفحتين — "
                "إذ يعيدك النظام إلى صفحة يستطيع دورك فتحها. أما الحساب الذي له دور آخر يعطي "
                "الاستعلامات فتبقى له الصفحتان."))

    h2(doc, T("4.5 How the system decides who can run a query",
              "٤.٥ كيف يقرر النظام من يستطيع تشغيل الاستعلام"))
    para(doc, T("A user can run a query if at least one of these is true. They add up — none "
                "comes before another.",
                "يستطيع المستخدم تشغيل الاستعلام إذا تحقق واحد على الأقل مما يلي. وكلها تُجمع "
                "معًا، ولا يتقدّم أحدها على الآخر."))
    table(doc,
          [T("Way",
             "الطريقة"), T("How it is given",
                                         "كيف يُعطى")],
          [
              [T("Role", "الدور"),
               T("The query (or one of its groups) is given to a role the user has — and that "
                 "role has “Run queries”.",
                 "الاستعلام (أو إحدى مجموعاته) معطى لدور يملكه المستخدم، وهذا الدور يملك "
                 "«تنفيذ الاستعلامات».")],
              [T("User group", "مجموعة المستخدمين"),
               T("The query (or one of its groups) is given to a user group the user is in.",
                 "الاستعلام (أو إحدى مجموعاته) معطى لمجموعة مستخدمين ينتمي إليها المستخدم.")],
              [T("A person",
                 "شخص محدد"),
               T("The query (or one of its groups) is given to the user by name.",
                 "الاستعلام (أو إحدى مجموعاته) معطى للمستخدم نفسه باسمه.")],
              [T("Group", "المجموعة"),
               T("Whatever is given on a query group applies to every query in it. A query in "
                 "several groups can be reached through any of them.",
                 "ما يُعطى على مجموعة استعلامات يشمل كل استعلام فيها. والاستعلام الموجود في "
                 "عدة مجموعات يمكن الوصول إليه عن طريق أيٍّ منها.")],
              [T("Administrator", "المسؤول"),
               T("An administrator can run every query without being given it.",
                 "يستطيع المسؤول تشغيل كل استعلام دون أن يُعطى له.")],
          ],
          widths=[1.5, 5.1])
    note(doc, T("The query must also be Enabled, and the user must be allowed to use the "
                "database connection the query runs through (Chapter 13). Both are checked "
                "every time it runs, not only when the page opens.",
                "ويجب أيضًا أن يكون الاستعلام «نشطًا»، وأن يكون مسموحًا للمستخدم باستخدام "
                "اتصال قاعدة البيانات الذي يعمل الاستعلام من خلاله (الفصل ١٣). ويتحقق النظام "
                "من الأمرين في كل تشغيل، وليس عند فتح الصفحة فقط."))

    h2(doc, T("4.6 Which permission opens each page",
              "٤.٦ الصلاحية التي تفتح كل صفحة"))
    para(doc, T("The permission that opens each page. Roles are not named here on purpose — "
                "any role with the permission can open the page, including roles you create.",
                "الصلاحية التي تفتح كل صفحة. ولم نذكر الأدوار هنا عمدًا — فأي دور يملك "
                "الصلاحية يفتح الصفحة، بما فيها الأدوار التي تنشئها."))
    table(doc,
          [T("Page", "الصفحة"), T("Address", "العنوان"), T("Permission needed",
                                                                        "الصلاحية المطلوبة")],
          [
              [T("My Queries", "الاستعلامات"), "/user/queries",
               T("Run queries", "تنفيذ الاستعلامات")],
              [T("Run a query", "تنفيذ استعلام"), "/user/queries/…/execute",
               T("Run queries, and the query must be given to you",
                 "تنفيذ الاستعلامات، ويجب أن يكون الاستعلام معطى لك")],
              [T("Dashboards", "لوحات المعلومات"), "/user/dashboards",
               T("View dashboards", "عرض لوحات المعلومات")],
              [T("View a dashboard", "عرض لوحة معلومات"), "/user/dashboards/…",
               T("View dashboards, and the dashboard must be given to you",
                 "عرض لوحات المعلومات، ويجب أن تكون اللوحة معطاة لك")],
              [T("My History", "سجلي"), "/user/history",
               T("Run queries", "تنفيذ الاستعلامات")],
              [T("My Schedules", "مهامي المجدولة"), "/user/schedules",
               T("Anyone added to a task as a viewer",
                 "كل من أُضيف إلى مهمة كمشاهد")],
              [T("Manage Queries", "إدارة الاستعلامات"), "/admin/queries",
               T("See the query list", "عرض قائمة الاستعلامات")],
              [T("Create / edit query", "إنشاء/تعديل استعلام"),
               "/admin/queries/create, /edit/…",
               T("Create and edit queries", "إنشاء الاستعلامات وتعديلها")],
              [T("Manage query access", "إدارة صلاحيات الاستعلام"), "/admin/queries/…/roles",
               T("Grant an individual query", "منح استعلام مفرد")],
              [T("Query Groups", "مجموعات الاستعلامات"), "/admin/query-groups",
               T("See the query list", "عرض قائمة الاستعلامات")],
              [T("Create / edit group", "إنشاء/تعديل مجموعة"),
               "/admin/query-groups/create, /edit/…",
               T("Create and edit query groups", "إنشاء مجموعات الاستعلامات وتعديلها")],
              [T("Manage group access", "إدارة صلاحيات المجموعة"),
               "/admin/query-groups/…/access",
               T("Grant a query group", "منح مجموعة استعلامات")],
              [T("Reports", "التقارير"), "/admin/reports",
               T("See the report list", "عرض قائمة التقارير")],
              [T("Create / edit report", "إنشاء/تعديل تقرير"),
               "/admin/reports/create, /edit/…",
               T("Create and edit reports", "إنشاء التقارير وتعديلها")],
              [T("Manage report access", "إدارة صلاحيات التقرير"), "/admin/reports/…/access",
               T("Grant a report", "منح صلاحية تقرير")],
              [T("Dashboards (admin)", "لوحات المعلومات (الإدارة)"), "/admin/dashboards",
               T("See the dashboard list", "عرض قائمة لوحات المعلومات")],
              [T("Create / edit dashboard", "إنشاء/تعديل لوحة معلومات"),
               "/admin/dashboards/create, /edit/…",
               T("Create and edit dashboards", "إنشاء لوحات المعلومات وتعديلها")],
              [T("Manage dashboard access", "إدارة صلاحيات لوحة المعلومات"),
               "/admin/dashboards/…/access",
               T("Grant a dashboard", "منح صلاحية لوحة معلومات")],
              [T("Scheduled Tasks", "المهام المجدولة"), "/admin/scheduled-tasks",
               T("See every scheduled task", "عرض جميع المهام المجدولة")],
              [T("Create / edit task", "إنشاء/تعديل مهمة"),
               "/admin/scheduled-tasks/create, /edit/…",
               T("Create and run scheduled tasks", "إنشاء المهام المجدولة وتشغيلها")],
              [T("Task run history", "سجل تشغيل المهمة"), "/admin/scheduled-tasks/…/runs",
               T("See every scheduled task", "عرض جميع المهام المجدولة")],
              [T("Execution Logs", "سجلات التنفيذ"), "/admin/logs",
               T("Read execution logs", "قراءة سجلات التنفيذ")],
              [T("System Audit", "سجل عمليات النظام"), "/admin/system-audit",
               T("Read the system audit trail", "قراءة سجل عمليات النظام")],
              [T("Settings", "الإعدادات"), "/admin/settings",
               T("Change system settings, or manage roles", "تغيير إعدادات النظام أو إدارة الأدوار")],
              [T("Users", "المستخدمون"), "/admin/users",
               T("See the user list", "عرض قائمة المستخدمين")],
              [T("User Groups", "مجموعات المستخدمين"), "/admin/user-groups",
               T("See user groups", "عرض مجموعات المستخدمين")],
              [T("Create / edit user group", "إنشاء/تعديل مجموعة مستخدمين"),
               "/admin/user-groups/create, /edit/…",
               T("Create user groups", "إنشاء مجموعات المستخدمين")],
              [T("AD Users", "مستخدمو Active Directory"), "/admin/ad-users",
               T("Search and import from AD", "البحث والاستيراد من Active Directory")],
              [T("DB Users", "مستخدمو قواعد البيانات"), "/admin/database-users",
               T("Manage database connections", "إدارة اتصالات قواعد البيانات")],
          ],
          widths=[1.8, 2.4, 2.4],
          font_size=8.5)

    page_break(doc)

    # ===================================================================== 5
    h1(doc, T("5. Running queries",
              "٥. تشغيل الاستعلامات"))
    para(doc, T("For: Users, and administrators running their own queries.",
                "لمن: المستخدمون، والمسؤولون عندما يشغّلون استعلاماتهم."), italic=True)

    h2(doc, T("5.1 My Queries", "٥.١ الاستعلامات"))
    para(doc, T("My Queries lists everything you have been given, arranged by query group. A "
                "query in more than one group appears under each of them. Queries that are in "
                "no group appear together at the end. Each card shows the query's name and "
                "description and a button to run it.",
                "تعرض صفحة «الاستعلامات» كل ما أُعطي لك، مرتّبًا حسب مجموعات الاستعلامات. "
                "ويظهر الاستعلام الموجود في أكثر من مجموعة تحت كلٍّ منها. وتظهر الاستعلامات "
                "التي ليست في أي مجموعة معًا في النهاية. وفي كل بطاقة اسم الاستعلام ووصفه وزر "
                "لتشغيله."))
    figure(doc, "60-my-queries",
           T("My Queries. Groups work like folders; the number next to each heading is how "
             "many queries you can use in it.",
             "صفحة «الاستعلامات». المجموعات تعمل مثل المجلدات، والرقم بجانب كل عنوان هو عدد "
             "الاستعلامات التي يمكنك استخدامها فيها."))
    para(doc, T("If the page is empty, nothing has been given to you yet — ask an "
                "administrator or an Access Manager to give you the query or its group, or to "
                "give it to a user group you are in.",
                "إذا كانت الصفحة فارغة فلم يُعطَ لك شيء بعد — اطلب من المسؤول أو مدير "
                "الصلاحيات أن يعطيك الاستعلام أو مجموعته، أو أن يعطيه لمجموعة مستخدمين أنت "
                "فيها."))
    note(doc, T("Reports are listed here too, in the same groups, each with a Report label "
                "and the number of its datasets. A report runs several queries at once and "
                "puts the results in one document; everything in this chapter applies to "
                "running one, and Chapter 7 explains what is different.",
                "تظهر التقارير هنا أيضًا، في المجموعات نفسها، وعلى كل منها كلمة «تقرير» وعدد "
                "مجموعات البيانات فيه. والتقرير يشغّل عدة استعلامات معًا ويضع نتائجها في "
                "مستند واحد؛ وكل ما في هذا الفصل ينطبق على تشغيله، ويشرح الفصل ٧ ما يختلف "
                "فيه."))

    h2(doc, T("5.2 Filling in parameters", "٥.٢ تعبئة معايير البحث"))
    para(doc, T("Opening a query shows a form made from the parameters the administrator set "
                "up. Required boxes must be filled in before the Execute button can be used.",
                "عند فتح الاستعلام يظهر نموذج فيه معايير البحث التي جهّزها المسؤول. ويجب ملء "
                "الخانات المطلوبة قبل أن يعمل زر التنفيذ."))
    figure(doc, "61-execute-parameters",
           T("A query's parameter form. Dates have a calendar, yes/no values have a switch, "
             "and required boxes show their own error messages.",
             "نموذج معايير البحث. للتواريخ تقويم، ولقيم نعم/لا مفتاح، والخانات المطلوبة تعرض "
             "رسالة الخطأ الخاصة بها."))
    table(doc,
          [T("Parameter type", "نوع معيار البحث"), T("How it appears", "كيف يظهر"),
           T("Notes", "ملاحظات")],
          [
              [T("String", "نص"), T("A text box", "حقل نصي"),
               T("Does not accept SQL words or special symbols.",
                 "لا يقبل كلمات SQL ولا الرموز الخاصة.")],
              [T("Number", "رقم"), T("A numeric box", "حقل رقمي"),
               T("Accepts digits only.", "يقبل الأرقام فقط.")],
              [T("Date", "تاريخ"), T("A box with a calendar picker", "حقل مع تقويم"),
               T("Pick from the calendar or type.", "اختر من التقويم أو اكتب مباشرة.")],
              [T("Boolean",
                 "نعم/لا"), T("A switch", "مفتاح تبديل"),
               T("Off, unless the administrator set a default.",
                 "مغلق، ما لم يضع المسؤول قيمة افتراضية.")],
              [T("Dropdown", "قائمة منسدلة"), T("A list of choices", "قائمة خيارات"),
               T("The choices are either a fixed list or come from another query.",
                 "الخيارات إما قائمة ثابتة أو تأتي من استعلام آخر.")],
          ],
          widths=[1.3, 2.0, 3.3])
    para(doc, T("A parameter may have a default value, which is filled in for you and can be "
                "changed. Lists whose choices come from a query show a loading bar while they "
                "load.",
                "قد يكون لمعيار البحث قيمة افتراضية تُملأ لك ويمكنك تغييرها. والقوائم التي "
                "تأتي خياراتها من استعلام تُظهر شريط تحميل أثناء تحميلها."))

    h2(doc, T("5.3 Multi-value parameters", "٥.٣ معايير البحث متعددة القيم"))
    para(doc, T("A String or Dropdown parameter can be set to accept several values. A String "
                "parameter then takes a list separated by commas, and a Dropdown lets you "
                "pick more than one item. The query then matches any of the values.",
                "يمكن جعل معيار البحث النصي أو القائمة المنسدلة يقبل أكثر من قيمة. فيقبل "
                "المعيار النصي حينها قيمًا تفصل بينها فواصل، وتسمح القائمة المنسدلة باختيار "
                "أكثر من عنصر. ثم يطابق الاستعلام أيًّا من هذه القيم."))
    figure(doc, "64-execute-multivalue",
           T("A String parameter that accepts several values. The hint under the box shows "
             "how to separate them with commas.",
             "معيار بحث نصي يقبل أكثر من قيمة. والتلميح أسفل الخانة يوضّح كيف تفصل بينها "
             "بفواصل."))

    h2(doc, T("5.4 Reading the results", "٥.٤ قراءة النتائج"))
    para(doc, T("A query that reads data runs once. The full result is kept on the server, "
                "and the table asks for one page at a time — so moving between pages, sorting "
                "and filtering never run the query again, and never send the whole result to "
                "your browser.",
                "الاستعلام الذي يقرأ البيانات يعمل مرة واحدة. وتُحفظ النتيجة كاملة على "
                "الخادم، ويطلب الجدول صفحة واحدة في كل مرة — فلا يعمل الاستعلام من جديد عند "
                "التنقل بين الصفحات أو الترتيب أو التصفية، ولا تُرسل النتيجة كاملة إلى "
                "متصفحك."))
    figure(doc, "62-execute-results",
           T("The result table. The line under the title shows the number of rows and how "
             "long the query took; the second header row filters each column.",
             "جدول النتائج. السطر تحت العنوان يعرض عدد الصفوف والمدة التي استغرقها الاستعلام، "
             "والصف الثاني في رأس الجدول يصفّي كل عمود."))
    bullets(doc, [
        (T("Sort — ", "الترتيب — "),
         T("click a column heading. Sorting applies to the whole result, not just the page on "
           "screen.",
           "انقر على عنوان العمود. ويُرتَّب كامل النتيجة، وليس الصفحة المعروضة فقط.")),
        (T("Filter a column — ", "تصفية عمود — "),
         T("type in the box under its heading. It finds values that contain what you typed, "
           "ignores capital and small letters, and works together with the other columns' "
           "filters.",
           "اكتب في الخانة تحت عنوانه. فيبحث عن القيم التي تحتوي ما كتبته، دون فرق بين الأحرف "
           "الكبيرة والصغيرة، ويعمل مع تصفية الأعمدة الأخرى معًا.")),
        (T("Page — ",
           "الصفحات — "),
         T("the page buttons at the bottom; 10, 25, 50 or 100 rows per page.",
           "أزرار الصفحات في الأسفل: ١٠ أو ٢٥ أو ٥٠ أو ١٠٠ صف في الصفحة.")),
        (T("Number of rows — ",
           "عدد الصفوف — "),
         T("the line under the title shows the total, and how many rows match your filters "
           "when that is different.",
           "السطر تحت العنوان يعرض العدد الكلي، وعدد الصفوف المطابقة للتصفية إذا كان مختلفًا.")),
    ])
    note(doc, T("Leaving the results page throws away the result kept on the server. If you "
                "press the browser's Back button the result is gone — run the query again to "
                "get it back.",
                "عند مغادرة صفحة النتائج تُحذف النتيجة المحفوظة على الخادم. فإذا ضغطت زر "
                "الرجوع في المتصفح تضيع النتيجة — شغّل الاستعلام مرة أخرى لاستعادتها."))

    h2(doc, T("5.5 Exporting results", "٥.٥ تصدير النتائج"))
    para(doc, T("Export makes the file from the result already kept on the server, so "
                "exporting after viewing does not run the query again. The whole result is "
                "exported, not just the page on screen.",
                "يصنع التصدير الملف من النتيجة المحفوظة على الخادم، فلا يعمل الاستعلام مرة "
                "أخرى عند التصدير بعد العرض. ويُصدَّر كامل النتيجة، وليس الصفحة المعروضة فقط."))
    figure(doc, "63-export-menu", T("The five export formats.",
                                    "أنواع ملفات التصدير الخمسة."))
    table(doc,
          [T("Format",
             "نوع الملف"), T("Use it for", "تُستخدم لـ")],
          [
              ["Excel (.xlsx)",
               T("Working with the numbers yourself; the usual choice for tables.",
                 "العمل على الأرقام بنفسك، وهو الاختيار المعتاد للجداول.")],
              ["CSV (.csv)", T("Loading into another system.",
                               "إدخال البيانات في نظام آخر.")],
              ["PDF (.pdf)",
               T("Sending a copy that cannot be changed and is ready to print.",
                 "إرسال نسخة لا تتغير وجاهزة للطباعة.")],
              ["Word (.docx)",
               T("A formatted report. Uses the query's own Word template if the administrator "
                 "uploaded one, otherwise the system's standard one.",
                 "تقرير منسّق. يستخدم قالب Word الخاص بالاستعلام إن رفعه المسؤول، وإلا "
                 "فالقالب العام للنظام.")],
              ["JSON (.json)", T("Passing to another program.",
                                 "تمريرها إلى برنامج آخر.")],
          ],
          widths=[1.4, 5.2])

    h2(doc, T("5.6 Long-running queries", "٥.٦ الاستعلامات طويلة التنفيذ"))
    para(doc, T("A query the administrator marked long-running runs in the background instead "
                "of keeping the page waiting. While it runs you see a timer, and a Cancel "
                "button next to it.",
                "الاستعلام الذي جعله المسؤول «طويل التنفيذ» يعمل في الخلفية بدل أن تبقى "
                "الصفحة منتظرة. وأثناء تشغيله يظهر عدّاد للوقت، وبجانبه زر إلغاء."))
    para(doc, T("Cancel really stops the query on the database; it does not just leave the "
                "page. This mode is there so slow queries are not cut off by the network; the "
                "only limit left is the query's own time limit.",
                "زر الإلغاء يوقف الاستعلام فعلًا على قاعدة البيانات، ولا يكتفي بترك الصفحة. "
                "وهذا الوضع موجود حتى لا تنقطع الاستعلامات البطيئة بسبب الشبكة، ويبقى الحد "
                "الوحيد هو الوقت الأقصى للاستعلام نفسه."))
    para(doc, T("The result of a background run is kept by the server, not by your page, and "
                "not forever: it is thrown away after it has not been used for a while, and "
                "it is lost if the server restarts while the query runs or before you finish "
                "reading. The page tells you clearly when that happens — just run the query "
                "again.",
                "نتيجة التشغيل في الخلفية يحفظها الخادم وليس صفحتك، ولا تبقى إلى الأبد: فهي "
                "تُحذف إذا لم تُستخدم لفترة، وتضيع إذا أُعيد تشغيل الخادم أثناء عمل الاستعلام "
                "أو قبل أن تنتهي من قراءتها. وتخبرك الصفحة بذلك بوضوح إن حدث — وكل ما عليك هو "
                "تشغيل الاستعلام مرة أخرى."))

    h2(doc, T("5.7 Queries that change data",
              "٥.٧ الاستعلامات التي تغيّر البيانات"))
    para(doc, T("A query may be an INSERT, UPDATE or DELETE. These work in two steps, so you "
                "can see what will change before anything is saved.",
                "قد يكون الاستعلام من نوع INSERT أو UPDATE أو DELETE. وهذه تعمل على خطوتين، "
                "لترى ما سيتغيّر قبل أن يُحفظ أي شيء."))
    figure(doc, "65-execute-write-form",
           T("The form of a query that changes data. It looks like any other query; the "
             "difference shows after you run it.",
             "نموذج استعلام يغيّر البيانات. يبدو مثل أي استعلام آخر، ويظهر الفرق بعد التشغيل."))
    figure(doc, "66-execute-write-filled",
           T("The same form with its parameters filled in.",
             "النموذج نفسه بعد ملء معايير البحث."))
    numbered(doc, [
        T("Press Execute. The query is tried and then undone — nothing has changed yet.",
          "اضغط «تنفيذ». يُجرَّب الاستعلام ثم يُلغى — ولم يتغيّر شيء بعد."),
        T("The confirmation panel shows how many rows would change, and for UPDATE and DELETE "
          "it lists the actual rows.",
          "تعرض لوحة التأكيد عدد الصفوف التي ستتغيّر، وتعرض في UPDATE وDELETE الصفوف نفسها."),
        T("Press Confirm & Commit to make the change for real, or Cancel to stop. Cancel "
          "leaves the data exactly as it was.",
          "اضغط «تأكيد واعتماد» لتنفيذ التغيير فعلًا، أو «إلغاء» للتوقف. والإلغاء يترك "
          "البيانات كما كانت تمامًا."),
    ])
    figure(doc, "67-execute-write-preview",
           T("The confirmation panel. The number of rows and the rows themselves come from a "
             "real run that was then undone.",
             "لوحة التأكيد. عدد الصفوف والصفوف نفسها جاءت من تشغيل حقيقي ثم أُلغي."))
    note(doc, T("Nothing is saved until you press Confirm & Commit. Closing the page at the "
                "preview step is always safe.",
                "لا يُحفظ أي تغيير حتى تضغط «تأكيد واعتماد». وإغلاق الصفحة في خطوة المعاينة "
                "آمن دائمًا."))

    h2(doc, T("5.8 Running without the preview",
              "٥.٨ التشغيل دون معاينة"))
    para(doc, T("The preview takes extra time and can be slow on large tables. If an "
                "administrator allowed it for a query, a “Run directly without preview” box "
                "appears above the Execute button. Ticking it saves the change in one step — "
                "the button turns red and says Run & Commit, and the note under it warns that "
                "there will be no confirmation step.",
                "المعاينة تأخذ وقتًا إضافيًا وقد تكون بطيئة على الجداول الكبيرة. وإذا سمح "
                "المسؤول بذلك لاستعلام ما، يظهر مربع «التنفيذ مباشرة دون معاينة» فوق زر "
                "التنفيذ. وعند تحديده يُحفظ التغيير في خطوة واحدة — فيصبح الزر أحمر ومكتوبًا "
                "عليه «تنفيذ واعتماد»، وتحذّرك الملاحظة تحته أنه لن تكون هناك خطوة تأكيد."))
    note(doc, T("With the box ticked there is no second chance. Leave it empty unless you run "
                "the same query often and know exactly what it changes.",
                "عند تحديد المربع لا توجد فرصة ثانية. اتركه فارغًا ما لم تكن تشغّل الاستعلام "
                "نفسه كثيرًا وتعرف بالضبط ما يغيّره."),
        kind="warning")

    h2(doc, T("5.9 Your execution history",
              "٥.٩ سجل التشغيل الخاص بك"))
    para(doc, T("History lists every query you have run: when, with which parameters, how "
                "long it took, how many rows came back, and whether it worked. The parameters "
                "are shown for every kind of query — SELECT, INSERT, UPDATE or DELETE — as "
                "small “name = value” labels; if there are many, you see the first three and "
                "“+N more”.",
                "تعرض صفحة «السجل» كل استعلام شغّلته: متى، وبأي معايير بحث، وكم استغرق، وكم "
                "صفًّا أعاد، وهل نجح. وتظهر معايير البحث لكل أنواع الاستعلامات — SELECT "
                "وINSERT وUPDATE وDELETE — في بطاقات صغيرة «الاسم = القيمة»، وإذا كانت كثيرة "
                "تظهر أول ثلاثة ثم «+N أخرى»."))
    figure(doc, "70-execution-history",
           T("My Execution History. Each run lists its parameters; Rows before opens the rows "
             "an UPDATE or DELETE changed, as they were before; the eye button opens the "
             "run's details.",
             "سجل التشغيل. يعرض كل تشغيل معايير البحث، وزر «الصفوف قبل التغيير» يفتح الصفوف "
             "التي غيّرها UPDATE أو DELETE كما كانت قبل التغيير، وزر العين يفتح تفاصيل "
             "التشغيل."))
    para(doc, T("The eye button at the end of a row opens everything saved about that run in "
                "one window — the same window administrators see, explained in 14.1.",
                "زر العين في نهاية الصف يفتح كل ما حُفظ عن ذلك التشغيل في نافذة واحدة — وهي "
                "النافذة نفسها التي يراها المسؤولون، والمشروحة في ١٤.١."))

    h2(doc, T("5.10 Scheduled task status", "٥.١٠ حالة المهام المجدولة"))
    para(doc, T("If an administrator added you as a viewer of a scheduled task — by name, "
                "through one of your roles, or through a user group you are in — a Schedules "
                "item appears in your top bar. It shows when each run started, whether it "
                "worked, what started it, and the files it made. You can download those files "
                "only if you were also allowed to download.",
                "إذا أضافك المسؤول كمشاهد لمهمة مجدولة — باسمك، أو عن طريق أحد أدوارك، أو عن "
                "طريق مجموعة مستخدمين أنت فيها — يظهر «المهام المجدولة» في الشريط العلوي. "
                "وفيه وقت بدء كل تشغيل، وهل نجح، وما الذي بدأه، والملفات التي أنتجها. ولا "
                "يمكنك تنزيل هذه الملفات إلا إذا سُمح لك بالتنزيل أيضًا."))

    page_break(doc)

    # ===================================================================== 6
    h1(doc, T("6. Managing queries", "٦. إدارة الاستعلامات"))
    para(doc, T("For: Administrators. Access Managers can see the list but cannot create, "
                "change, delete or export.",
                "لمن: المسؤولون. ويرى مديرو الصلاحيات القائمة لكن لا يستطيعون الإنشاء أو "
                "التعديل أو الحذف أو التصدير."), italic=True)

    h2(doc, T("6.1 The query list", "٦.١ قائمة الاستعلامات"))
    figure(doc, "10-manage-queries",
           T("Manage Queries. Filters across the top, one row per query, and the buttons for "
             "each row at the end.",
             "إدارة الاستعلامات. عوامل التصفية في الأعلى، وصف لكل استعلام، وأزرار كل صف في "
             "نهايته."))
    para(doc, T("The columns are Name, Description, Type, Status, Groups, DB User and the "
                "number of parameters. Groups lists every group the query is in. Five filters "
                "make the list shorter, and they stay set when you leave the page and come "
                "back:",
                "الأعمدة هي: الاسم والوصف والنوع والحالة والمجموعات ومستخدم قاعدة البيانات "
                "وعدد معايير البحث. ويعرض عمود «المجموعات» كل مجموعة فيها الاستعلام. وخمس "
                "أدوات تصفية تختصر القائمة، وتبقى كما هي عند مغادرة الصفحة والعودة إليها:"))
    bullets(doc, [
        (T("Search — ", "البحث — "),
         T("looks in the name, description, group and database connection.",
           "يبحث في الاسم والوصف والمجموعة واتصال قاعدة البيانات.")),
        (T("Status — ", "الحالة — "), T("All, Active or Disabled.", "الكل أو نشط أو معطَّل.")),
        (T("Type — ", "النوع — "), T("SELECT, INSERT, UPDATE, DELETE or Other.",
                                     "SELECT أو INSERT أو UPDATE أو DELETE أو أخرى.")),
        (T("Group — ", "المجموعة — "),
         T("one group — a query in several groups shows up under each of them — or Ungrouped.",
           "مجموعة واحدة — والاستعلام الموجود في عدة مجموعات يظهر تحت كلٍّ منها — أو غير "
           "مُصنَّف.")),
        (T("DB User — ", "مستخدم قاعدة البيانات — "),
         T("the database connection the query runs through.",
           "اتصال قاعدة البيانات الذي يعمل الاستعلام من خلاله.")),
    ])
    para(doc, T("A Clear filters button appears whenever a filter is on, so you do not "
                "mistake a filter for missing queries.",
                "يظهر زر «مسح التصفية» كلما كانت هناك تصفية، حتى لا تظن أن بعض الاستعلامات "
                "مفقودة."))
    table(doc,
          [T("Button",
             "الزر"), T("What it does",
                                                     "ماذا يفعل"),
           T("Who sees it", "من يراه")],
          [
              [T("Edit", "تعديل"), T("Opens the query for editing", "يفتح الاستعلام للتعديل"),
               "Admin"],
              [T("Copy", "نسخ"),
               T("Opens a new query form already filled in from this query",
                 "يفتح نموذج استعلام جديد مملوءًا من هذا الاستعلام"), "Admin"],
              [T("Manage access", "إدارة الصلاحيات"),
               T("Give the query to roles, user groups and users",
                 "إعطاء الاستعلام لأدوار ومجموعات مستخدمين ومستخدمين"),
               T("Anyone with “Grant an individual query”",
                 "من يملك «منح استعلام مفرد»")],
              [T("Export", "تصدير"),
               T("Downloads this query as a JSON file",
                 "ينزّل هذا الاستعلام في ملف JSON"), "Admin"],
              [T("Delete", "حذف"),
               T("Deletes the query, its parameters and everyone's access to it",
                 "يحذف الاستعلام ومعايير البحث فيه وصلاحيات الوصول إليه"), "Admin"],
          ],
          widths=[1.3, 3.4, 1.9])

    h2(doc, T("6.2 Creating a query", "٦.٢ إنشاء استعلام"))
    figure(doc, "13-query-create",
           T("The Create Dynamic Query form.",
             "نموذج إنشاء استعلام جديد."))
    table(doc,
          [T("Field", "الحقل"), T("Meaning", "المعنى")],
          [
              [T("Name", "الاسم"),
               T("The name users see. Required.",
                 "الاسم الذي يراه المستخدمون. مطلوب.")],
              [T("Description", "الوصف"),
               T("Shown under the name on My Queries. Required.",
                 "يظهر تحت الاسم في صفحة «الاستعلامات». مطلوب.")],
              [T("SQL Query", "استعلام SQL"),
               T("The query text, in an editor with colours (see below). Write parameters as "
                 "@name. The longest text allowed is set by Maximum SQL length in System "
                 "Settings — 4,000 characters unless an administrator changes it.",
                 "نص الاستعلام، في محرر ملوّن (انظر أدناه). اكتب معايير البحث بالشكل ‎@name‎. "
                 "وأقصى طول مسموح يحدده «الحد الأقصى لطول نص SQL» في إعدادات النظام — ٤٠٠٠ "
                 "حرف ما لم يغيّره المسؤول.")],
              [T("Timeout (seconds)", "المهلة (بالثواني)"),
               T("How long the query may run. 0 means no limit.",
                 "أطول وقت مسموح لتشغيل الاستعلام. والقيمة ٠ تعني بلا حد.")],
              [T("Long-running query", "استعلام طويل التنفيذ"),
               T("Runs the query in the background, with a timer and a Cancel button. Use it "
                 "for slow queries; leave it off otherwise.",
                 "يشغّل الاستعلام في الخلفية مع عدّاد للوقت وزر إلغاء. استخدمه للاستعلامات "
                 "البطيئة، واتركه مغلقًا لغيرها.")],
              [T("Allow running without confirmation", "السماح بالتنفيذ دون تأكيد"),
               T("Only for queries that change data. Shows users the “Run directly without "
                 "preview” box. On at the start.",
                 "للاستعلامات التي تغيّر البيانات فقط. يُظهر للمستخدمين مربع «التنفيذ مباشرة "
                 "دون معاينة». وهو مفعّل في البداية.")],
              [T("Save before-change values", "حفظ القيم قبل التغيير"),
               T("UPDATE and DELETE only. Saves every changed row as it was, with the "
                 "execution log. On at the start; turn it off for queries that change very "
                 "many rows.",
                 "لـ UPDATE وDELETE فقط. يحفظ كل صف تغيّر كما كان، مع سجل التنفيذ. وهو مفعّل "
                 "في البداية، وأغلقه للاستعلامات التي تغيّر عددًا كبيرًا جدًا من الصفوف.")],
              [T("Database User", "مستخدم قاعدة البيانات"),
               T("Which connection the query runs through. Default uses the system's own "
                 "connection.",
                 "الاتصال الذي يعمل الاستعلام من خلاله. والخيار الافتراضي يستخدم اتصال النظام "
                 "نفسه.")],
              [T("Groups", "المجموعات"),
               T("The folders the query appears under on My Queries. Pick as many as you "
                 "need, or none to leave it Ungrouped. Giving someone any one of these groups "
                 "gives them the query.",
                 "المجلدات التي يظهر فيها الاستعلام في صفحة «الاستعلامات». اختر منها ما "
                 "تحتاج، أو لا شيء ليبقى غير مُصنَّف. وإعطاء أي مجموعة منها لشخص يعطيه "
                 "الاستعلام.")],
              [T("Enabled", "نشط"),
               T("Only when editing. Nobody can run a disabled query.",
                 "عند التعديل فقط. ولا يستطيع أحد تشغيل استعلام معطَّل.")],
          ],
          widths=[1.9, 4.7])
    figure(doc, "14b-query-groups-field",
           T("The Groups field. Outstanding Invoices is in both Finance and Invoicing and "
             "Customer Service, so it appears in both folders, and either group gives access "
             "to it.",
             "حقل «المجموعات». استعلام «الفواتير المستحقة» موجود في «المالية والفواتير» "
             "و«خدمة العملاء» معًا، فيظهر في المجلدين، وأي من المجموعتين تعطي الوصول إليه."))
    note(doc, T("The system works out the query type from the SQL when you save — you do not "
                "choose it — so the Type column and the preview always match the SQL.",
                "يعرف النظام نوع الاستعلام من نص SQL عند الحفظ — ولا تختاره أنت — لذلك يتطابق "
                "عمود «النوع» والمعاينة دائمًا مع نص SQL."))

    h3(doc, T("The SQL editor", "محرر SQL"))
    para(doc, T("The SQL box is a code editor. SQL words, text in quotes, numbers and "
                "comments each have their own colour, and every @parameter stands out, so you "
                "can easily see what the query needs. It numbers the lines, shows the "
                "matching bracket, closes brackets and quotes for you, and suggests SQL words "
                "as you type — Enter takes the suggestion, Esc hides it. The counter under "
                "the box shows how many characters you have used out of the maximum.",
                "مربع SQL محرر للكود: لكلمات SQL والنصوص بين علامات التنصيص والأرقام "
                "والتعليقات ألوان مختلفة، وتظهر كل معايير البحث ‎@parameter‎ بوضوح، فترى "
                "بسهولة ما يحتاجه الاستعلام. وفيه أرقام للأسطر، ويُظهر القوس المقابل، ويغلق "
                "الأقواس وعلامات التنصيص تلقائيًا، ويقترح كلمات SQL أثناء الكتابة — زر Enter "
                "يقبل الاقتراح وزر Esc يخفيه. والعدّاد تحت المربع يبيّن عدد الأحرف التي "
                "استخدمتها من الحد الأقصى."))
    para(doc, T("For a long query, the expand button next to the label opens the same editor "
                "over almost the whole screen. Changes you make there go straight into the "
                "form — Done, Esc or the shrink button takes you back with every change kept. "
                "Nothing is saved until you press Save.",
                "للاستعلام الطويل، زر التكبير بجانب العنوان يفتح المحرر نفسه على معظم الشاشة. "
                "وما تغيّره هناك ينتقل مباشرة إلى النموذج — فزر «تم» أو Esc أو زر التصغير "
                "يعيدك مع بقاء كل التغييرات. ولا يُحفظ شيء حتى تضغط «حفظ»."))
    figure(doc, "14a-query-sql-expanded",
           T("The SQL opened in the large editor.", "نص SQL مفتوحًا في المحرر الكبير."))

    h3(doc, T("When a query cannot be saved",
              "عندما لا يمكن حفظ الاستعلام"))
    para(doc, T("Save checks the form first. If anything is wrong, a box above the buttons "
                "lists every problem in plain words — a missing description, a parameter name "
                "with a space in it, Parameter 2 without a display name — and it gets shorter "
                "as you fix them. If the server refuses to save, its reasons appear in the "
                "same box and stay until you try again.",
                "زر «حفظ» يفحص النموذج أولًا. فإن كان فيه خطأ، يظهر مربع فوق الأزرار فيه كل "
                "مشكلة بكلمات واضحة — وصف ناقص، أو اسم معيار فيه مسافة، أو المعيار ٢ بلا اسم "
                "ظاهر — ويقصر كلما أصلحت واحدة. وإذا رفض الخادم الحفظ تظهر أسبابه في المربع "
                "نفسه وتبقى حتى تحاول مرة أخرى."))
    para(doc, T("Three things are never allowed in a query's SQL:",
                "ثلاثة أشياء لا يُسمح بها أبدًا في نص SQL للاستعلام:"))
    bullets(doc, [
        (T("A semicolon. ", "الفاصلة المنقوطة. "),
         T("A saved query is one statement only, so do not put ; at the end.",
           "الاستعلام المحفوظ جملة واحدة فقط، فلا تضع ‎;‎ في نهايته.")),
        (T("A -- comment. ", "تعليق ‎--‎. "),
         T("Use a /* … */ comment instead.", "استخدم تعليق ‎/* … */‎ بدلًا منه.")),
        (T("System procedures. ",
           "إجراءات النظام. "),
         T("Names starting with XP_, SP_, DBMS_ or UTL_.",
           "الأسماء التي تبدأ بـ XP_ أو SP_ أو DBMS_ أو UTL_.")),
    ])
    para(doc, T("These are found as you type: the line turns red, the wrong characters are "
                "underlined, and a message under the box gives the line number and how to fix "
                "it. Show, next to the message in the box above the buttons, takes you "
                "straight to the place.",
                "وتُكتشف هذه أثناء الكتابة: يصبح السطر أحمر، ويُوضع خط تحت الأحرف الخاطئة، "
                "وتذكر رسالة تحت المربع رقم السطر وطريقة الإصلاح. وزر «إظهار» بجانب الرسالة "
                "في المربع فوق الأزرار يأخذك إلى المكان مباشرة."))
    figure(doc, "14c-query-save-problems",
           T("A save that was refused: the -- comment and the semicolon are marked in the "
             "editor and listed, with their line numbers, in the box above the buttons.",
             "حفظ مرفوض: تعليق ‎--‎ والفاصلة المنقوطة مميّزان في المحرر، ومذكوران مع أرقام "
             "أسطرهما في المربع فوق الأزرار."))

    h2(doc, T("6.3 Setting up parameters",
              "٦.٣ تجهيز معايير البحث"))
    para(doc, T("Add Parameter adds a parameter card. Each one needs a name — the @name used "
                "in the SQL — and a display name, which is the label the user sees.",
                "زر «إضافة معيار بحث» يضيف بطاقة معيار. ويحتاج كل معيار إلى اسم — وهو ‎@name‎ "
                "المستخدم في نص SQL — واسم ظاهر، وهو العنوان الذي يراه المستخدم."))
    figure(doc, "14-query-edit-parameters",
           T("The parameter editor, showing a Dropdown parameter whose choices come from "
             "another query.",
             "محرر معايير البحث، وفيه قائمة منسدلة تأتي خياراتها من استعلام آخر."))
    bullets(doc, [
        (T("Type — ", "النوع — "),
         T("String, Number, Date, Boolean or Dropdown.",
           "نص أو رقم أو تاريخ أو نعم/لا أو قائمة منسدلة.")),
        (T("Required — ", "مطلوب — "),
         T("the user cannot run the query until it is filled in.",
           "لا يستطيع المستخدم تشغيل الاستعلام حتى يملأه.")),
        (T("Default Value — ", "القيمة الافتراضية — "),
         T("already filled in for the user, who can still change it.",
           "تكون مملوءة مسبقًا للمستخدم، ويستطيع تغييرها.")),
        (T("Allow multiple values — ", "السماح بقيم متعددة — "),
         T("String and Dropdown only. The user gives several values and the query matches any "
           "of them; write the SQL as WHERE col IN (@name).",
           "للنص والقائمة المنسدلة فقط. يُدخل المستخدم أكثر من قيمة فيطابق الاستعلام أيًّا "
           "منها، ويُكتب نص SQL بالشكل ‎WHERE col IN (@name)‎.")),
    ])
    h3(doc, T("Dropdown parameters",
              "القوائم المنسدلة"))
    para(doc, T("A Dropdown parameter gets its choices from one of two places:",
                "تأخذ القائمة المنسدلة خياراتها من أحد مكانين:"))
    bullets(doc, [
        (T("Static list — ", "قائمة ثابتة — "),
         T("label and value pairs that you type. The value is what the SQL gets; the label is "
           "what the user sees.",
           "أزواج من عنوان وقيمة تكتبها بنفسك. فالقيمة هي ما يذهب إلى نص SQL، والعنوان هو ما "
           "يراه المستخدم.")),
        (T("From database query — ", "من استعلام قاعدة بيانات — "),
         T("pick another saved query and choose the column that gives the value and the "
           "column that gives the label. The list is loaded fresh every time the form opens.",
           "اختر استعلامًا محفوظًا آخر، وحدّد العمود الذي يعطي القيمة والعمود الذي يعطي "
           "العنوان. وتُحمَّل القائمة من جديد في كل مرة يُفتح فيها النموذج.")),
    ])

    h3(doc, T("Testing before you save",
              "التجربة قبل الحفظ"))
    para(doc, T("Test, next to Save, runs the query exactly as it is in the form right now — "
                "the SQL, the database user and the parameters — without saving anything. It "
                "asks for a value for each parameter (already filled with the defaults), then "
                "shows the result:",
                "زر «اختبار» بجانب «حفظ» يشغّل الاستعلام كما هو في النموذج الآن تمامًا — نص "
                "SQL ومستخدم قاعدة البيانات ومعايير البحث — دون حفظ أي شيء. فيطلب قيمة لكل "
                "معيار (مملوءة مسبقًا بالقيم الافتراضية)، ثم يعرض النتيجة:"))
    bullets(doc, [
        (T("SELECT — ", "SELECT — "),
         T("the first 100 rows.", "أول ١٠٠ صف.")),
        (T("INSERT, UPDATE or DELETE — ", "INSERT أو UPDATE أو DELETE — "),
         T("how many rows it would change and, for UPDATE and DELETE, which ones. The query "
           "really runs, but it is always undone afterwards, so nothing changes.",
           "عدد الصفوف التي ستتغيّر، وفي UPDATE وDELETE أيّها. يعمل الاستعلام فعلًا لكنه "
           "يُلغى دائمًا بعد ذلك، فلا يتغيّر شيء.")),
        (T("A database error — ", "خطأ من قاعدة البيانات — "),
         T("shown exactly as the database gives it, such as ORA-00942 for a table that does "
           "not exist, so you can fix the SQL and test again.",
           "يظهر كما أرسلته قاعدة البيانات، مثل ORA-00942 لجدول غير موجود، فتصلح النص وتجرّب "
           "مرة أخرى.")),
    ])
    figure(doc, "14b-query-test-run",
           T("A test run of the Orders by Period and Status query.",
             "تشغيل تجريبي لاستعلام «الطلبات حسب الفترة والحالة»."))
    para(doc, T("You do not need a name or description to test, and closing the window leaves "
                "the form exactly as it was. Each test is recorded in the System Audit log as "
                "“Tested query (unsaved)”, with its SQL.",
                "لا تحتاج إلى اسم ووصف لتجربة الاستعلام، وإغلاق النافذة يترك النموذج كما كان "
                "تمامًا. ويُسجَّل كل اختبار في سجل عمليات النظام باسم «اختبار استعلام (غير "
                "محفوظ)» مع نص SQL."))
    note(doc, T("Only SELECT, INSERT, UPDATE and DELETE can be tested. MERGE, CREATE, ALTER, "
                "DROP and PL/SQL are refused, because Oracle saves those changes at once and "
                "they cannot be undone. A test INSERT that takes a number from a sequence "
                "(NEXTVAL) still uses up that number — Oracle never gives those numbers back.",
                "لا يمكن تجربة إلا SELECT وINSERT وUPDATE وDELETE. وتُرفض MERGE وCREATE "
                "وALTER وDROP وPL/SQL، لأن Oracle يحفظ هذه التغييرات فورًا ولا يمكن إلغاؤها. "
                "وتجربة INSERT تأخذ رقمًا من تسلسل (NEXTVAL) تستهلك هذا الرقم رغم ذلك — فلا "
                "يعيد Oracle هذه الأرقام أبدًا."),
        kind="warning")

    h2(doc, T("6.4 Word export templates", "٦.٤ قوالب تصدير Word"))
    para(doc, T("When a user exports results to Word, the layout comes from a template. A "
                "query can have its own template; otherwise the system's standard one is "
                "used.",
                "عندما يصدّر المستخدم النتائج إلى Word يأتي الشكل من قالب. ويمكن أن يكون "
                "للاستعلام قالبه الخاص، وإلا يُستخدم القالب العام للنظام."))
    bullets(doc, [
        (T("Per query — ", "لكل استعلام — "),
         T("upload a .docx file on the query's edit form. Buttons to download or remove it "
           "appear once one is added.",
           "ارفع ملف ‎.docx‎ من نموذج تعديل الاستعلام. ويظهر زرّا التنزيل والحذف بعد إضافة "
           "القالب.")),
        (T("For the whole system — ",
           "للنظام كله — "),
         T("the Default Word Template menu on the query list.",
           "قائمة «قالب Word الافتراضي» في صفحة الاستعلامات.")),
    ])
    figure(doc, "12-default-template-menu",
           T("The Default Word Template menu. The top line says whether the built-in layout "
             "or an uploaded template is being used now.",
             "قائمة «قالب Word الافتراضي». السطر العلوي يبيّن هل المستخدم الآن هو الشكل "
             "الجاهز أم قالب مرفوع."))
    para(doc, T("Inside a template, these markers are replaced when the file is made:",
                "داخل القالب، تُستبدل هذه العلامات عند صنع الملف:"))
    table(doc,
          [T("Marker", "العلامة"), T("Replaced with", "تُستبدل بـ")],
          [
              ["{{RESULTS}}",
               T("The result table. Put the marker inside a table to choose how the result "
                 "looks: the row above it sets the look of the header, its own row sets the "
                 "look of the data rows, and a row below it sets every second row.",
                 "جدول النتائج. ضع العلامة داخل جدول لتختار شكل النتيجة: الصف الذي فوقها يحدد "
                 "شكل العناوين، وصفّها يحدد شكل صفوف البيانات، والصف الذي تحتها يحدد شكل "
                 "الصفوف المتبادلة.")],
              ["{{QUERY_NAME}}", T("The query's name.", "اسم الاستعلام.")],
              ["{{GENERATED_AT}}", T("When the file was made.",
                                     "وقت صنع الملف.")],
              ["{{@generated_by}}",
               T("The username of the person who exported the file. For a scheduled task: the "
                 "admin who pressed Run now, otherwise the person who created the task.",
                 "اسم المستخدم الذي صدّر الملف. وفي المهام المجدولة: المسؤول الذي ضغط «تشغيل "
                 "الآن»، وإلا فالشخص الذي أنشأ المهمة.")],
              ["{{ROW_COUNT}}", T("The number of rows.", "عدد الصفوف.")],
              ["{{@paramName}}",
               T("The value the user entered for that parameter.",
                 "القيمة التي أدخلها المستخدم لذلك المعيار.")],
              ["{{PARAMS}}",
               T("Every parameter as “Display Name: value”, one per line.",
                 "كل معيار بالشكل «الاسم الظاهر: القيمة»، واحد في كل سطر.")],
          ],
          widths=[1.7, 4.9])
    para(doc, T("Markers in page headers and footers are replaced too.",
                "وتُستبدل العلامات في رأس الصفحة وتذييلها أيضًا."))

    h2(doc, T("6.5 Backing up queries and bringing them back",
              "٦.٥ نسخ الاستعلامات احتياطيًا واستعادتها"))
    figure(doc, "11-backup-menu",
           T("The Backup menu on the query list.",
             "قائمة «نسخة احتياطية» في صفحة الاستعلامات."))
    bullets(doc, [
        (T("Export all queries — ", "تصدير جميع الاستعلامات — "),
         T("downloads every query as one JSON file.",
           "ينزّل كل الاستعلامات في ملف JSON واحد.")),
        (T("Export (row button) — ",
           "تصدير (زر الصف) — "),
         T("downloads one query as a JSON file.",
           "ينزّل استعلامًا واحدًا في ملف JSON.")),
        (T("Import from backup… — ", "الاستيراد من نسخة احتياطية… — "),
         T("brings queries back from an export file. Administrators only.",
           "يعيد الاستعلامات من ملف تصدير. للمسؤولين فقط.")),
    ])
    para(doc, T("An export file refers to other things by their names, not by hidden internal "
                "numbers, so a backup can be brought into a different system:",
                "ملف التصدير يشير إلى الأشياء الأخرى بأسمائها، وليس بأرقام داخلية مخفية، لذلك "
                "يمكن إدخال النسخة الاحتياطية إلى نظام آخر:"))
    table(doc,
          [T("Item",
             "العنصر"), T("On import", "عند الاستيراد")],
          [
              [T("Query groups", "مجموعات الاستعلامات"),
               T("Every group the query is in, found by name; any group that does not exist "
                 "is created.",
                 "كل مجموعة فيها الاستعلام، يُبحث عنها بالاسم، وما لم يكن موجودًا منها يُنشأ.")],
              [T("Database connection", "اتصال قاعدة البيانات"),
               T("Found by name. If it is not there, the query uses the default connection "
                 "and you get a warning.",
                 "يُبحث عنه بالاسم. وإن لم يكن موجودًا يستخدم الاستعلام الاتصال الافتراضي "
                 "وتظهر لك ملاحظة.")],
              [T("Roles and users", "الأدوار والمستخدمون"),
               T("Found by name; any that are not found are left out, and you are told.",
                 "يُبحث عنها بالاسم، وما لم يوجد منها يُترك ويُذكر لك.")],
              [T("User groups", "مجموعات المستخدمين"),
               T("Found by name; access for a group that does not exist here is left out, and "
                 "you are told. Who is in a group is never exported.",
                 "يُبحث عنها بالاسم، والصلاحية لمجموعة غير موجودة هنا تُترك ويُذكر لك ذلك. "
                 "ولا يُصدَّر أبدًا من في المجموعة.")],
              [T("Query behind a dropdown",
                 "الاستعلام الذي تأتي منه القائمة المنسدلة"),
               T("Connected after every query in the file has been added, so a dropdown can "
                 "point at another query in the same file.",
                 "يُربط بعد إضافة كل استعلامات الملف، فتستطيع القائمة المنسدلة أن تشير إلى "
                 "استعلام آخر في الملف نفسه.")],
          ],
          widths=[1.7, 4.9])
    note(doc, T("Import never replaces anything. A query whose name is already used comes in "
                "as “Name (imported)”, so you can undo a wrong import by deleting what it "
                "added. The result window lists everything imported, everything renamed and "
                "everything that could not be found. Reports and dashboards have their own "
                "Backup menus (7.9 and 8.7), and Import accepts a file from any of the three.",
                "الاستيراد لا يستبدل أي شيء أبدًا. فالاستعلام الذي اسمه مستخدم من قبل يدخل "
                "باسم «الاسم (imported)»، فتستطيع إلغاء الاستيراد الخاطئ بحذف ما أضافه. وتعرض "
                "نافذة النتيجة كل ما استُورد، وكل ما تغيّر اسمه، وكل ما لم يُعثر عليه. "
                "وللتقارير ولوحات المعلومات قوائم «نسخ احتياطي» خاصة بها (٧.٩ و٨.٧)، ويقبل "
                "الاستيراد ملفًا من أيٍّ منها."))
    note(doc, T("Database passwords are never written to an export file — only the "
                "connection's name. After bringing queries into a new system, create the "
                "connection there with its own password; because the names match, the queries "
                "connect to it.",
                "لا تُكتب كلمات مرور قواعد البيانات في ملف التصدير أبدًا — بل اسم الاتصال "
                "فقط. وبعد إدخال الاستعلامات إلى نظام جديد، أنشئ الاتصال هناك بكلمة مروره، "
                "ولأن الأسماء متطابقة ترتبط به الاستعلامات."),
        kind="warning")

    page_break(doc)

    # ===================================================================== 7
    h1(doc, T("7. Reports", "٧. التقارير"))
    para(doc, T("For: everyone runs them; Administrators build them.",
                "لمن: يشغّلها الجميع، وينشئها المسؤولون."), italic=True)
    para(doc, T("A query answers one question and gives back one table. A report answers "
                "several at once: it runs a set of queries together, with one set of "
                "parameters, and puts the results in a single document — a monthly pack, a "
                "branch summary, a customer statement.",
                "الاستعلام يجيب عن سؤال واحد ويعيد جدولًا واحدًا. أما التقرير فيجيب عن عدة "
                "أسئلة معًا: يشغّل مجموعة استعلامات في وقت واحد، بمعايير بحث واحدة، ويضع "
                "النتائج في مستند واحد — مثل تقرير شهري، أو ملخص فرع، أو كشف حساب عميل."))
    note(doc, T("Reports are not a separate thing to learn. They appear on the same My "
                "Queries page as queries, in the same query groups, and access to a group "
                "gives its reports too. For the person running it, a report is just an item "
                "that shows several tables instead of one.",
                "التقارير ليست شيئًا منفصلًا تحتاج إلى تعلّمه. فهي تظهر في صفحة «الاستعلامات» "
                "نفسها مع الاستعلامات، وفي المجموعات نفسها، ومن يُعطى مجموعة يحصل على "
                "تقاريرها أيضًا. وبالنسبة لمن يشغّله، التقرير مجرد عنصر يعرض عدة جداول بدل "
                "جدول واحد."))

    h2(doc, T("7.1 Running a report", "٧.١ تشغيل التقرير"))
    para(doc, T("Open it from My Queries just as you would a query. Fill in the parameters "
                "once — every query in the report uses them — and press Run.",
                "افتحه من صفحة «الاستعلامات» كما تفتح أي استعلام. املأ معايير البحث مرة واحدة "
                "— فكل الاستعلامات في التقرير تستخدمها — ثم اضغط «تشغيل»."))
    figure(doc, "46-report-run",
           T("A report on screen: one parameter form, the charts, then one tab for each "
             "section.",
             "التقرير على الشاشة: نموذج معايير واحد، ثم الرسوم البيانية، ثم تبويب لكل قسم."))
    bullets(doc, [
        (T("Each section is a tab. ", "كل قسم في تبويب. "),
         T("Each section is the same page-by-page table used for a query result, with its own "
           "column filters and sorting, so a large section only loads one page of rows at a "
           "time.",
           "كل قسم هو جدول النتائج نفسه المقسّم إلى صفحات، وله تصفية الأعمدة والترتيب الخاصة "
           "به، فلا يُحمَّل من القسم الكبير إلا صفحة واحدة في كل مرة.")),
        (T("Charts come first. ", "الرسوم البيانية أولًا. "),
         T("They are drawn from the same numbers as the sections below them.",
           "تُرسم من الأرقام نفسها التي تعرضها الأقسام تحتها.")),
        (T("Warnings appear above the results. ", "تظهر التنبيهات فوق النتائج. "),
         T("A section that reached a row limit, or one query that failed while the others "
           "worked, is shown here instead of stopping the whole report.",
           "القسم الذي وصل إلى حد عدد الصفوف، أو الاستعلام الذي فشل بينما نجح الباقي، يُذكر "
           "هنا بدل أن يتوقف التقرير كله.")),
        (T("Export writes the whole document. ", "التصدير يكتب المستند كاملًا. "),
         T("Not just the tab you are looking at — every section, in the report's order.",
           "وليس التبويب الذي أمامك فقط — بل كل الأقسام، بترتيب التقرير.")),
    ])

    h2(doc, T("7.2 What a report is made of",
              "٧.٢ مكوّنات التقرير"))
    para(doc, T("A report is a list of datasets, each with a name — the dataset key. The Word "
                "template uses this key to know where to put that dataset's table. A key can "
                "only have letters, digits and underscores, and no two datasets in a report "
                "can share one.",
                "التقرير قائمة من مجموعات البيانات، لكل منها اسم — هو «مفتاح» المجموعة. "
                "ويستخدم قالب Word هذا المفتاح ليعرف أين يضع جدول تلك المجموعة. والمفتاح "
                "يتكون من حروف وأرقام وشرطة سفلية فقط، ولا يتكرر داخل التقرير الواحد."))
    figure(doc, "23b-report-details",
           T("The Details tab: the report's name, its group, and the three limits for a run.",
             "تبويب «التفاصيل»: اسم التقرير ومجموعته والحدود الثلاثة للتشغيل."))
    figure(doc, "24-report-datasets",
           T("The Datasets tab. Each dataset names a saved query and says how its parameters "
             "are filled in.",
             "تبويب «مجموعات البيانات». كل مجموعة تحدد استعلامًا محفوظًا وكيف تُملأ معايير "
             "البحث فيه."))
    table(doc,
          [T("Kind", "النوع"), T("What it does",
                                      "ماذا يفعل"),
           T("Use it for", "يُستخدم في")],
          [
              [T("Query", "استعلام"),
               T("Runs one saved query and gives back its rows.",
                 "يشغّل استعلامًا محفوظًا واحدًا ويعيد صفوفه."),
               T("A separate section shown with the others.",
                 "قسم مستقل يظهر مع باقي الأقسام.")],
              [T("Join", "دمج"),
               T("Puts two datasets that were already loaded into one wider table, matching "
                 "them on a column from each. Inner or left.",
                 "يضع مجموعتَي بيانات محمّلتين من قبل في جدول واحد أعرض، بمطابقة عمود من كل "
                 "منهما. داخلي أو أيسر."),
               T("Putting two queries' columns side by side on one row.",
                 "وضع أعمدة استعلامين جنبًا إلى جنب في سطر واحد.")],
              [T("Detail", "تفصيل"),
               T("Runs its query again for each row of a parent dataset, passing a column of "
                 "that row as a parameter.",
                 "يشغّل استعلامه مرة لكل صف من مجموعة بيانات رئيسية، ويمرّر عمودًا من ذلك "
                 "الصف كمعيار بحث."),
               T("Main list and details — each customer followed by their own records.",
                 "قائمة رئيسية وتفاصيل — كل عميل وتحته سجلاته الخاصة.")],
          ], widths=[1.0, 2.9, 2.3])
    note(doc, T("A detail dataset runs its query once for every parent row. Ten parent rows "
                "means ten runs, so keep the parent list short. Maximum detail rows on the "
                "Details tab is the limit: the run handles that many parent rows and says in "
                "a warning how many it skipped, instead of failing. Set it to 0 and every "
                "parent row is handled, however many there are — then only the report's time "
                "limit stops the run, so think before raising it.",
                "مجموعة التفاصيل تشغّل استعلامها مرة لكل صف في القائمة الرئيسية. فعشرة صفوف "
                "تعني عشر مرات تشغيل، لذا اجعل القائمة الرئيسية قصيرة. و«الحد الأقصى لصفوف "
                "التفاصيل» في تبويب «التفاصيل» هو الحد: يعالج التشغيل هذا العدد من الصفوف "
                "ويذكر في ملاحظة كم صفًا تركه، بدل أن يفشل. وإذا وضعته ٠ تُعالج كل الصفوف "
                "مهما كان عددها — وحينها لا يوقف التشغيل إلا الوقت الأقصى للتقرير، ففكّر قبل "
                "رفعه."), kind="warning")

    h2(doc, T("7.3 One set of parameters for every query",
              "٧.٣ معايير بحث واحدة لكل الاستعلامات"))
    para(doc, T("The report has its own parameters — the ones the reader fills in — and each "
                "dataset says where each of its query's parameters gets its value. That is "
                "how one date range can feed five queries that each gave it a different name.",
                "للتقرير معايير بحث خاصة به — وهي التي يملؤها القارئ — وكل مجموعة بيانات تحدد "
                "من أين يأخذ كل معيار في استعلامها قيمته. وهكذا يمكن لفترة زمنية واحدة أن تصل "
                "إلى خمسة استعلامات، لكلٍّ منها اسم مختلف لها."))
    figure(doc, "25-report-parameters",
           T("The Parameters tab. These are the boxes the reader sees.",
             "تبويب «معايير البحث». هذه هي الخانات التي يراها القارئ."))
    table(doc,
          [T("Source", "المصدر"), T("Meaning", "المعنى")],
          [
              [T("Report parameter", "معيار من التقرير"),
               T("Uses the value the reader typed in that report parameter.",
                 "يأخذ القيمة التي كتبها القارئ في معيار التقرير هذا.")],
              [T("Constant", "قيمة ثابتة"),
               T("Always the same value, set when the report was built. Useful for a status "
                 "filter or a row count the reader should not change.",
                 "قيمة واحدة لا تتغير، تُحدد عند إنشاء التقرير. مفيدة لتصفية حالة أو عدد صفوف "
                 "لا يجب أن يغيّره القارئ.")],
              [T("Parent column",
                 "عمود الصف الأصل"),
               T("Detail datasets only: the value of that column in the parent row being "
                 "handled.",
                 "لمجموعات التفاصيل فقط: قيمة هذا العمود في الصف الأصل الذي تتم معالجته.")],
          ], widths=[1.6, 4.6])
    note(doc, T("A query parameter that is not linked to anything uses its own default value. "
                "If it has no default and is required, the report will not run — the Datasets "
                "tab warns you when you save.",
                "معيار الاستعلام غير المربوط بشيء يأخذ قيمته الافتراضية. فإن لم تكن له قيمة "
                "افتراضية وكان مطلوبًا فلن يعمل التقرير — ويحذّرك تبويب «مجموعات البيانات» من "
                "ذلك عند الحفظ."))

    h2(doc, T("7.4 Charts", "٧.٤ الرسوم البيانية"))
    para(doc, T("A chart draws one dataset: one column gives the categories, and one or more "
                "columns give the values. It appears above the sections on screen, and "
                "wherever its marker is in the template.",
                "يرسم المخطط مجموعة بيانات واحدة: عمود يعطي الفئات، وعمود أو أكثر يعطي القيم. "
                "ويظهر فوق الأقسام على الشاشة، وفي مكان علامته داخل القالب."))
    figure(doc, "26-report-charts",
           T("The Charts tab. The value columns are separated by commas.",
             "تبويب «الرسوم البيانية». تُفصل أعمدة القيم بفواصل."))
    table(doc,
          [T("Type", "النوع"), T("Good for",
                                      "مناسب لـ")],
          [
              [T("Columns", "أعمدة رأسية"),
               T("Comparing a value across a few categories.",
                 "مقارنة قيمة بين عدد قليل من الفئات.")],
              [T("Bars", "أعمدة أفقية"),
               T("The same, when the category names are long.",
                 "الشيء نفسه، عندما تكون أسماء الفئات طويلة.")],
              [T("Line", "خط بياني"),
               T("A value changing over time.",
                 "قيمة تتغير مع الوقت.")],
              [T("Pie", "دائري"),
               T("Parts of one whole. One series only.",
                 "أجزاء من شيء واحد. سلسلة قيم واحدة فقط.")],
              [T("Doughnut", "دائري مجوف"),
               T("Like a pie, with a hole in the middle. One series only.",
                 "مثل الدائري، مع فراغ في وسطه. سلسلة قيم واحدة فقط.")],
              [T("Area", "مساحة"),
               T("A line with the space under it filled in — an amount over time.",
                 "خط تُملأ المساحة تحته — كمية تتغير مع الوقت.")],
              [T("Stacked columns", "أعمدة مكدسة"),
               T("Each category's values stacked in one column, so the column shows the total "
                 "and its parts.",
                 "قيم كل فئة فوق بعضها في عمود واحد، فيُظهر العمود المجموع وأجزاءه.")],
              [T("Stacked bars", "أشرطة مكدسة"),
               T("The same, lying across.",
                 "الشيء نفسه، بشكل أفقي.")],
              [T("Columns + line", "أعمدة + خط"),
               T("All series except the last are columns; the last is a line with its own "
                 "scale — for example sales against profit %. Needs at least two series.",
                 "كل السلاسل أعمدة ما عدا الأخيرة، فتُرسم خطًا له مقياسه الخاص — مثل المبيعات "
                 "مقابل نسبة الربح. ويحتاج إلى سلسلتين على الأقل.")],
          ], widths=[1.4, 4.8])
    para(doc, T("Every type is a real Word chart in the exported file, so you can still "
                "change its look in Word later.",
                "كل نوع منها مخطط Word حقيقي في الملف المصدَّر، فيمكنك تغيير شكله في Word "
                "لاحقًا."))
    bullets(doc, [
        (T("Use the column names the query gives back. ",
           "استخدم أسماء الأعمدة كما يعيدها الاستعلام. "),
         T("If the query renames a column with AS, use the new name — that is what the table "
           "shows.",
           "فإن كان الاستعلام يغيّر اسم عمود باستخدام AS، فاستخدم الاسم الجديد — فهو الذي "
           "يظهر في الجدول.")),
        (T("A cell that is not a number is left empty, ",
           "الخلية التي ليست رقمًا تُترك فارغة، "),
         T("not drawn as zero — an empty month does not pull the line down to the bottom.",
           "ولا تُرسم صفرًا — فالشهر الفارغ لا يُنزل الخط إلى الأسفل.")),
        (T("Most categories to plot ", "أقصى عدد للفئات "),
         T("keeps a chart of a thousand rows easy to read; the rows past it are still in the "
           "section's table.",
           "يجعل مخطط الألف صف سهل القراءة، وتبقى الصفوف الزائدة موجودة في جدول القسم.")),
    ])
    note(doc, T("A chart cannot have the same key as a dataset — otherwise {{RESULTS:x}} and "
                "{{CHART:x}} would not know which x is meant.",
                "لا يمكن أن يكون للمخطط مفتاح مجموعة بيانات نفسه — وإلا لم تعرف "
                "‎{{RESULTS:x}}‎ و‎{{CHART:x}}‎ أيّ x هو المقصود."))

    h2(doc, T("7.5 The Word template", "٧.٥ قالب Word"))
    para(doc, T("The layout is a Word document you design yourself and upload. The system "
                "fills in the data; the template decides how it looks — fonts, headings, page "
                "size, a letterhead, anything Word can do.",
                "الشكل مستند Word تصممه بنفسك وترفعه. فالنظام يضع البيانات، والقالب يحدد "
                "شكلها — الخطوط والعناوين وحجم الصفحة ورأس الشركة، وكل ما يستطيعه Word."))
    figure(doc, "27-report-template",
           T("The Template tab: upload, download, or start from a ready-made template.",
             "تبويب «القالب»: رفع، أو تنزيل، أو البدء من قالب جاهز."))
    para(doc, T("Start with Download starter template. It is made for this report and already "
                "has the right keys, so the markers are correct before you change anything.",
                "ابدأ بـ«تنزيل قالب مبدئي». فهو مصنوع لهذا التقرير وفيه المفاتيح الصحيحة، "
                "فتكون العلامات صحيحة قبل أن تغيّر أي شيء."), bold=True)
    table(doc,
          [T("Marker", "العلامة"), T("Becomes",
                                            "تصبح")],
          [
              ["{{RESULTS:key}}",
               T("That dataset's table. The table row that holds the marker is the example: "
                 "its fonts, borders and colours are copied to every data row.",
                 "جدول تلك المجموعة. وصف الجدول الذي فيه العلامة هو المثال: تُنسخ خطوطه "
                 "وحدوده وألوانه إلى كل صف بيانات.")],
              ["{{ROW_COUNT:key}}",
               T("How many rows that dataset gave back.",
                 "عدد الصفوف التي أعادتها تلك المجموعة.")],
              ["{{VALUE:key.Column}}",
               T("One cell from the first row — for a big headline number.",
                 "خلية واحدة من الصف الأول — لعرض رقم كبير مهم.")],
              ["{{CHART:key}}",
               T("The chart with that key.", "المخطط الذي يحمل ذلك المفتاح.")],
              ["{{#EACH:key}} … {{/EACH}}",
               T("Repeats everything between the two markers once for each row of that "
                 "dataset. Inside it, {{FIELD:Column}} is that row's cell, and a detail "
                 "dataset's table follows its own parent row.",
                 "يكرر كل ما بين العلامتين مرة لكل صف من تلك المجموعة. وداخلها تعني "
                 "‎{{FIELD:Column}}‎ خلية الصف الحالي، ويأتي جدول التفاصيل تحت صفه الرئيسي.")],
              ["{{#IFANY:key}}, {{#IFEMPTY:key}}",
               T("Keeps or removes a whole block depending on whether the dataset gave back "
                 "anything — so an empty section can hide its heading too.",
                 "يُبقي جزءًا كاملًا أو يحذفه حسب وجود نتائج في المجموعة — فيمكن للقسم الفارغ "
                 "أن يُخفي عنوانه أيضًا.")],
              ["{{REPORT_NAME}}, {{GENERATED_AT}}, {{@generated_by}}, {{@parameter}}",
               T("The report's name, when it was made, who exported it, and any parameter the "
                 "reader typed.",
                 "اسم التقرير، ووقت صنعه، ومن صدّره، وأي معيار بحث كتبه القارئ.")],
          ], widths=[2.0, 4.2])
    note(doc, T("Put a marker in normal text or in a table cell. A marker inside a Word text "
                "box is left as it is, and you are warned when you upload — text boxes are "
                "not filled in.",
                "ضع العلامة في نص عادي أو في خلية جدول. أما العلامة داخل مربع نص في Word "
                "فتبقى كما هي وتظهر لك ملاحظة عند الرفع — لأن مربعات النص لا تُملأ."), kind="warning")

    h2(doc, T("7.6 Exporting a report", "٧.٦ تصدير التقرير"))
    para(doc, T("Word and PDF both use the template — the PDF is the same document turned "
                "into PDF, so it prints the way you designed it. Excel puts each dataset on "
                "its own sheet, and CSV and JSON keep the datasets apart instead of mixing "
                "them.",
                "يستخدم Word وPDF القالب — فملف PDF هو المستند نفسه بعد تحويله، فيُطبع بالشكل "
                "الذي صممته. أما Excel فيضع كل مجموعة بيانات في ورقة خاصة، ويُبقي CSV وJSON "
                "المجموعات منفصلة بدل خلطها."))
    note(doc, T("A file type must be allowed in two places: the report must allow it, and "
                "your role must have the matching export permission. The menu only shows what "
                "both allow.",
                "يجب أن يُسمح بنوع الملف في مكانين: أن يسمح به التقرير، وأن يملك دورك صلاحية "
                "التصدير المناسبة. ولا تعرض القائمة إلا ما يسمح به الاثنان."))

    h2(doc, T("7.7 Sending a report on a schedule",
              "٧.٧ إرسال التقرير في أوقات محددة"))
    para(doc, T("A scheduled task can include a report just like a query: add an item, choose "
                "Report instead of Query, pick it, and give its parameters fixed values. The "
                "file is saved in the task's output folder at the times you set — see Chapter "
                "11.",
                "يمكن أن تشمل المهمة المجدولة تقريرًا مثل الاستعلام تمامًا: أضف عنصرًا، واختر "
                "«تقرير» بدل «استعلام»، ثم حدده وأعطِ معايير البحث قيمًا ثابتة. ويُحفظ الملف "
                "في مجلد المهمة في الأوقات التي تحددها — انظر الفصل ١١."))

    h2(doc, T("7.8 Who can open a report",
              "٧.٨ من يستطيع فتح التقرير"))
    para(doc, T("Access is given the same three ways as for a query — by role, by user group, "
                "or to people by name — and access to the query group the report is in gives "
                "access to the report too.",
                "تُعطى الصلاحية بالطرق الثلاث نفسها كما في الاستعلامات — بالدور، أو بمجموعة "
                "المستخدمين، أو لأشخاص بأسمائهم — ومن يُعطى مجموعة الاستعلامات التي فيها "
                "التقرير يحصل على التقرير أيضًا."))
    figure(doc, "28-report-access",
           T("Report access. Being able to open the report is only the first check.",
             "صلاحية التقرير. وفتح التقرير هو الفحص الأول فقط."))
    note(doc, T("Opening a report never gives you the queries inside it. When the report "
                "runs, each dataset's query is checked separately, and a dataset the reader "
                "could not run themselves is skipped with a warning — the rest of the report "
                "still works. So a report can never be used to get around query permissions.",
                "فتح التقرير لا يعطيك أبدًا الاستعلامات التي بداخله. فعند التشغيل يُفحص "
                "استعلام كل مجموعة بيانات وحده، والمجموعة التي لا يستطيع القارئ تشغيلها بنفسه "
                "تُترك مع ملاحظة — ويعمل باقي التقرير. لذلك لا يمكن استخدام التقرير للتحايل "
                "على صلاحيات الاستعلامات."), kind="warning")

    h2(doc, T("7.9 Moving reports to another system", "٧.٩ نقل التقارير إلى نظام آخر"))
    para(doc, T("The Backup menu on the report list exports every report to one JSON file, "
                "and each report's own menu exports just that one. Import from file brings a "
                "file in — on this system or another. You need “Export and import report "
                "definitions”.",
                "قائمة «النسخ الاحتياطي» في صفحة التقارير تصدّر كل التقارير في ملف JSON واحد، "
                "و«تصدير» في قائمة كل تقرير يصدّر ذلك التقرير وحده. و«الاستيراد من ملف» يُدخل "
                "الملف — إلى هذا النظام أو إلى غيره. وتحتاج إلى صلاحية «تصدير تعريفات "
                "التقارير واستيرادها»."))
    bullets(doc, [
        (T("Include the queries they use — ", "تضمين الاستعلامات التي تستخدمها — "),
         T("also saves the queries the datasets and dropdowns use, so the file works on a "
           "system that does not have them. Because the file then contains their SQL, this "
           "also needs “Export and import query definitions”. Without it, the import connects "
           "each dataset to a query with the same name that is already there.",
           "يحفظ أيضًا الاستعلامات التي تستخدمها مجموعات البيانات والقوائم المنسدلة، فيعمل "
           "الملف على نظام ليست فيه. ولأن الملف يحتوي حينها نص SQL الخاص بها، فهذا يحتاج "
           "أيضًا إلى «تصدير تعريفات الاستعلامات واستيرادها». وبدونها يربط الاستيراد كل "
           "مجموعة بيانات باستعلام له الاسم نفسه موجود هناك.")),
        (T("The Word template goes with the report; ",
           "ينتقل قالب Word مع التقرير؛ "),
         T("database passwords never do.", "أما كلمات مرور قواعد البيانات فلا تنتقل أبدًا.")),
        (T("Nothing is replaced. ",
           "لا يُستبدل أي شيء. "),
         T("A name that is already used comes in as “Name (imported)”, and anything that "
           "cannot be found is listed as a warning instead of stopping the import — a report "
           "whose dataset query is missing is skipped, and you are told why.",
           "الاسم المستخدم من قبل يدخل باسم «الاسم (imported)»، وما لم يُعثر عليه يُذكر في "
           "ملاحظة بدل أن يوقف الاستيراد — والتقرير الذي ينقصه استعلام إحدى مجموعات بياناته "
           "يُترك، ويُذكر لك السبب.")),
    ])

    page_break(doc)

    # ===================================================================== 8
    h1(doc, T("8. Dashboards", "٨. لوحات المعلومات"))
    para(doc, T("For: everyone watches them; Administrators build them.",
                "لمن: يتابعها الجميع، وينشئها المسؤولون."), italic=True)
    para(doc, T("A report runs when someone asks for it. A dashboard is left open: a page of "
                "tiles — big numbers, charts and short tables — each fed by a saved query and "
                "each updating itself every so often, so the page stays up to date without "
                "anyone pressing Run. It is the screen on the wall of the sales office, or "
                "the tab a manager keeps open all day.",
                "التقرير يعمل عندما يطلبه أحد. أما لوحة المعلومات فتبقى مفتوحة: صفحة من "
                "العناصر — أرقام كبيرة ورسوم بيانية وجداول قصيرة — يعمل كل عنصر من استعلام "
                "محفوظ ويتحدّث تلقائيًا كل فترة، فتبقى الصفحة محدّثة دون أن يضغط أحد «تشغيل». "
                "هي الشاشة المعلّقة على جدار مكتب المبيعات، أو الصفحة التي يتركها المدير "
                "مفتوحة طوال اليوم."))
    note(doc, T("A dashboard never runs anything a query could not. Every tile is a saved "
                "query that reads data, run with the viewer's own permissions, so a dashboard "
                "is a new way to look at data — not a way to see more of it.",
                "لوحة المعلومات لا تشغّل شيئًا لا يستطيعه الاستعلام. فكل عنصر استعلام محفوظ "
                "يقرأ البيانات، ويعمل بصلاحيات المشاهد نفسه، فاللوحة طريقة جديدة لرؤية "
                "البيانات — وليست طريقة لرؤية المزيد منها."))

    h2(doc, T("8.1 Opening a dashboard", "٨.١ فتح لوحة المعلومات"))
    para(doc, T("Dashboards has its own item in the top bar. It lists the dashboards shared "
                "with you, with how many tiles each has and how often it updates.",
                "لـ«لوحات المعلومات» زر خاص في الشريط العلوي. ويعرض اللوحات المشارَكة معك، مع "
                "عدد العناصر في كل منها وكم مرة تتحدّث."))
    figure(doc, "47-my-dashboards",
           T("The dashboards shared with you.", "لوحات المعلومات المشارَكة معك."))
    figure(doc, "48-dashboard-view",
           T("The Sales Overview dashboard: the filter bar, then sales by region, staff by "
             "department, a headcount number, top customers by segment, daily sales and a "
             "table of departments. Every tile updates by itself.",
             "لوحة أداء المبيعات: شريط التصفية، ثم المبيعات حسب المنطقة، والموظفون حسب "
             "الإدارة، ورقم عدد الموظفين، وأكبر العملاء حسب الشريحة، والمبيعات اليومية، وجدول "
             "الإدارات. وكل عنصر يتحدّث وحده."))
    bullets(doc, [
        (T("Filters are above the tiles. ",
           "عوامل التصفية فوق العناصر. "),
         T("Change them and press Apply; nothing changes until you do. Then every tile that "
           "uses a filter runs its query again straight away with the new value — the "
           "database does the filtering, so you never see old rows that were just hidden — "
           "and the other tiles stay as they are. If someone looked at the same filter values "
           "a short while ago, their result is shown at once instead of running the query "
           "again. Reset puts the filters back to the dashboard's starting values.",
           "غيّرها ثم اضغط «تطبيق»، ولا يتغير شيء قبل ذلك. بعدها يشغّل كل عنصر يستخدم التصفية "
           "استعلامه من جديد فورًا بالقيمة الجديدة — فقاعدة البيانات هي التي تصفّي، فلا ترى "
           "صفوفًا قديمة مخفية فقط — وتبقى العناصر الأخرى كما هي. وإذا شاهد أحد قيم التصفية "
           "نفسها قبل قليل، تظهر نتيجته فورًا بدل تشغيل الاستعلام من جديد. وزر «إعادة الضبط» "
           "يعيد التصفية إلى قيم البداية في اللوحة.")),
        (T("A date filter can move with time. ",
           "يمكن أن يتحرك تاريخ التصفية مع الوقت. "),
         T("Dates like today, start of this month or start of last month keep their meaning "
           "as time passes, so a dashboard left open overnight still shows “this month” the "
           "next morning.",
           "تواريخ مثل اليوم، وبداية هذا الشهر، وبداية الشهر الماضي تبقى صحيحة مع مرور الوقت، "
           "فاللوحة المفتوحة طوال الليل تعرض «هذا الشهر» في الصباح التالي أيضًا.")),
        (T("The filters are part of the page address. ",
           "قيم التصفية جزء من عنوان الصفحة. "),
         T("Copy the address and you have a link to exactly this view — save it as a "
           "bookmark, or send it to a colleague who has access.",
           "انسخ العنوان فيصبح لديك رابط لهذا العرض بالضبط — احفظه في المفضلة أو أرسله لزميل "
           "لديه صلاحية.")),
        (T("Each tile says how old its data is. ",
           "كل عنصر يذكر وقت بياناته. "),
         T("If an update fails, the tile keeps the last data it had and says so, instead of "
           "going blank.",
           "وإذا فشل التحديث يُبقي العنصر آخر بيانات وصلته ويخبرك بذلك، بدل أن يصبح فارغًا.")),
        (T("Refresh updates every tile now. ",
           "زر «تحديث» يحدّث كل العناصر الآن. "),
         T("Everyone looking at the same dashboard with the same filters shares the results, "
           "so a tile that someone else updated a moment ago may come back the same.",
           "كل من يشاهد اللوحة نفسها بالتصفية نفسها يتشاركون النتائج، فقد يعود عنصر حدّثه شخص "
           "آخر قبل لحظة دون تغيير.")),
    ])

    h2(doc, T("8.2 Reading a tile", "٨.٢ قراءة العنصر"))
    table(doc,
          [T("Tile", "العنصر"), T("Shows", "يعرض")],
          [
              [T("KPI number", "مؤشر رقمي"),
               T("One big number, how it changed compared with another value, and — when the "
                 "query gives a series of values — a small trend line under it.",
                 "رقم كبير واحد، وكم تغيّر مقارنةً بقيمة أخرى، وإذا أعاد الاستعلام سلسلة من "
                 "القيم يظهر تحته خط صغير يبيّن الاتجاه.")],
              [T("Charts", "المخططات"),
               T("Every chart a report draws — columns, bars, line, area, pie, doughnut, stacked "
                 "columns and bars, and columns + line — with the same rules; see 7.4.",
                 "كل مخطط يرسمه التقرير — الأعمدة والأشرطة والخط والمساحة والدائري والدائري "
                 "المجوف والأعمدة والأشرطة المكدسة والأعمدة مع الخط — وبالقواعد نفسها؛ انظر ٧.٤.")],
              [T("Gauge", "مقياس"),
               T("A KPI number drawn as a dial from zero to a maximum, with the target marked "
                 "on it.",
                 "مؤشر رقمي مرسوم على شكل عدّاد من الصفر إلى حد أعلى، والهدف معلَّم عليه.")],
              [T("Funnel", "قمع"),
               T("Steps from largest to smallest, each as a share of the first — enquiries, "
                 "then quotes, then orders.",
                 "خطوات من الأكبر إلى الأصغر، كل منها نسبة من الأولى — استفسارات ثم عروض "
                 "أسعار ثم طلبات.")],
              [T("Heatmap", "خريطة حرارية"),
               T("A grid of one category against another, each cell coloured by its value — "
                 "for example sales by region and month.",
                 "شبكة فيها فئة مقابل فئة أخرى، وكل خلية ملوّنة حسب قيمتها — مثل المبيعات حسب "
                 "المنطقة والشهر.")],
              [T("Table", "جدول"),
               T("The first rows of the result. The bottom line says how many there are in "
                 "total.",
                 "أول صفوف النتيجة، ويذكر السطر الأخير عددها الكلي.")],
          ], widths=[1.8, 4.4])
    para(doc, T("A KPI's change reads “Up 12% from …” or “Down 4% from …”, and is coloured by "
                "whether that is good news: for sales, going up is green; for overdue "
                "invoices, going up is red. A KPI with a target also says On target, Near "
                "target or Off target. In the Sales Overview dashboard, the Headcount tile "
                "has a target of 20.",
                "يظهر تغيّر المؤشر بالشكل «ارتفاع ١٢٪ عن …» أو «انخفاض ٤٪ عن …»، ويُلوَّن حسب "
                "كونه خبرًا جيدًا: فارتفاع المبيعات أخضر، وارتفاع الفواتير المتأخرة أحمر. "
                "والمؤشر الذي له هدف يذكر أيضًا «محقق» أو «قريب من الهدف» أو «دون الهدف». ففي "
                "لوحة أداء المبيعات، هدف عنصر «عدد الموظفين» هو ٢٠."))
    para(doc, T("Colour rules can make a KPI or a table cell green, orange or red. In the "
                "Departments table, a department with four or more staff is green and one "
                "with fewer than three is red. The rules are set when the dashboard is built.",
                "قواعد الألوان تستطيع أن تجعل المؤشر أو خلية الجدول أخضر أو برتقاليًا أو "
                "أحمر. ففي جدول «الإدارات» تظهر الإدارة التي فيها أربعة موظفين أو أكثر "
                "بالأخضر، والتي فيها أقل من ثلاثة بالأحمر. وتُحدَّد هذه القواعد عند إنشاء "
                "اللوحة."))
    note(doc, T("A tile whose query you are not allowed to run shows a message instead of its "
                "data, and the rest of the dashboard keeps working. So a dashboard can safely "
                "mix different teams' data: on Sales Overview, a salesperson sees the sales "
                "tiles, while the staff tiles, which use an HR query, stay closed to them.",
                "العنصر الذي لا يُسمح لك بتشغيل استعلامه يعرض رسالة بدل بياناته، ويستمر باقي "
                "اللوحة في العمل. لذلك يمكن للوحة أن تجمع بيانات فرق مختلفة بأمان: ففي لوحة "
                "أداء المبيعات يرى موظف المبيعات عناصر المبيعات، بينما تبقى عناصر الموظفين — "
                "التي تستخدم استعلامًا للموارد البشرية — مغلقة عليه."))

    h2(doc, T("8.3 Clicking, rows and downloads", "٨.٣ النقر والصفوف والتنزيل"))
    para(doc, T("Clicking a bar or a slice can do one of three things, chosen when the tile "
                "was built: filter the whole dashboard to that category, open a report about "
                "it, or show the rows behind the tile. On Sales Overview, clicking a region's "
                "column shows only that region in every tile that uses regions, and clicking "
                "a slice of Top Customers by Revenue opens the Monthly Sales Report.",
                "النقر على عمود أو شريحة يفعل واحدًا من ثلاثة أشياء يُختار عند إنشاء العنصر: "
                "تصفية اللوحة كلها على تلك الفئة، أو فتح تقرير عنها، أو عرض الصفوف التي جاء "
                "منها العنصر. ففي لوحة أداء المبيعات، النقر على عمود منطقة يُظهر تلك المنطقة "
                "فقط في كل العناصر التي تستخدم المناطق، والنقر على شريحة من «أكبر العملاء من "
                "حيث الإيرادات» يفتح التقرير الشهري للمبيعات."))
    para(doc, T("Every tile also has a menu in its corner. Show rows runs the tile's query in "
                "full and opens the result in a window. Download saves the same result to a "
                "file, in the file types the query allows and your role may export — the same "
                "two checks as on the query page.",
                "ولكل عنصر قائمة في زاويته. «عرض الصفوف» يشغّل استعلام العنصر كاملًا ويفتح "
                "النتيجة في نافذة، و«تنزيل» يحفظ النتيجة نفسها في ملف، بأنواع الملفات التي "
                "يسمح بها الاستعلام ويُسمح لدورك بتصديرها — وهما الفحصان نفساهما في صفحة "
                "الاستعلام."))

    h2(doc, T("8.4 Wall-screen mode", "٨.٤ وضع شاشة العرض"))
    para(doc, T("Wall-screen mode, a button at the top of the dashboard, hides the top bar "
                "and the filters and makes the text bigger, for a screen read from across a "
                "room. Full screen then hides the browser's own frame as well.",
                "«وضع شاشة العرض» — وهو زر أعلى اللوحة — يُخفي الشريط العلوي والتصفية ويكبّر "
                "الكتابة، لشاشة تُقرأ من بعيد. ثم يُخفي زر «ملء الشاشة» إطار المتصفح أيضًا."))
    figure(doc, "49-dashboard-wall-screen",
           T("The same dashboard in wall-screen mode.",
             "اللوحة نفسها في وضع شاشة العرض."))
    para(doc, T("For a screen nobody touches, put the mode in the page address instead. "
                "Adding ?tv=1 opens a dashboard straight in wall-screen mode. Adding &rotate= "
                "with dashboard ids separated by commas, and &every= with a number of seconds "
                "(at least 15; 60 if you leave it out), switches the screen from one "
                "dashboard to the next in turn. Take each id from that dashboard's address.",
                "لشاشة لا يلمسها أحد، ضع الوضع في عنوان الصفحة. فإضافة ‎?tv=1‎ تفتح اللوحة "
                "مباشرة في وضع شاشة العرض. وإضافة ‎&rotate=‎ مع أرقام اللوحات مفصولة بفواصل، "
                "و‎&every=‎ مع عدد الثواني (١٥ على الأقل، و٦٠ إن لم تكتبه)، تجعل الشاشة تنتقل "
                "من لوحة إلى التي بعدها بالتناوب. وخذ رقم كل لوحة من عنوانها."))
    note(doc, T("A wall screen signs in like anyone else. Give it its own account with only "
                "View dashboards, and give that account only the dashboards it shows — do not "
                "use the account of the person who set it up.",
                "شاشة العرض تدخل إلى النظام مثل أي مستخدم. فاعمل لها حسابًا خاصًا ليس له إلا "
                "صلاحية «عرض لوحات المعلومات»، وأعطِ هذا الحساب اللوحات التي يعرضها فقط — ولا "
                "تستخدم حساب الشخص الذي جهّزها."),
        kind="warning")

    h2(doc, T("8.5 Building a dashboard",
              "٨.٥ إنشاء لوحة المعلومات"))
    para(doc, T("Administrators build dashboards under Queries → Dashboards. The list shows "
                "each dashboard's tiles, filters, update time and status, and its menu opens, "
                "edits, shares or deletes it. Deleting a dashboard does not touch the queries "
                "its tiles use.",
                "ينشئ المسؤولون لوحات المعلومات من «الاستعلامات ← لوحات المعلومات». وتعرض "
                "القائمة عناصر كل لوحة وعوامل التصفية فيها وكم مرة تتحدّث وحالتها، ومن "
                "قائمتها تفتح اللوحة أو تعدّلها أو تشاركها أو تحذفها. وحذف اللوحة لا يؤثر على "
                "الاستعلامات التي تستخدمها عناصرها."))
    figure(doc, "29-dashboards-list",
           T("Queries → Dashboards.", "الاستعلامات ← لوحات المعلومات."))

    h3(doc, T("Details", "التفاصيل"))
    para(doc, T("The name and description people see, whether the dashboard is turned on, its "
                "place in the list, and how often its tiles update — between 30 seconds and "
                "an hour.",
                "الاسم والوصف اللذان يراهما الناس، وهل اللوحة مفعّلة، ومكانها في القائمة، وكم "
                "مرة تتحدّث عناصرها — بين ٣٠ ثانية وساعة."))
    figure(doc, "29a-dashboard-details",
           T("The Details tab.", "تبويب «التفاصيل»."))
    note(doc, T("The update time is also how long a tile's result is shared. Whether one "
                "person has the dashboard open or two hundred, each tile asks the database at "
                "most once in that time for each set of filter values — so the update time, "
                "not the number of viewers, decides how much work the dashboard gives the "
                "database.",
                "مدة التحديث هي أيضًا المدة التي تُشارَك فيها نتيجة العنصر. فسواء فتح اللوحة "
                "شخص واحد أو مئتان، لا يسأل كل عنصر قاعدة البيانات أكثر من مرة واحدة في هذه "
                "المدة لكل قيم تصفية — فمدة التحديث، وليس عدد المشاهدين، هي التي تحدد حجم "
                "العمل على قاعدة البيانات."))

    h3(doc, T("Filters", "عوامل التصفية"))
    para(doc, T("A filter is set up once for the whole dashboard, with the same types as a "
                "query parameter: text, number, date, yes/no, or a list whose options are "
                "fixed or come from a query. Its name (letters, digits and underscores) is "
                "what appears in the address; its label is what people see.",
                "تُجهَّز عامل التصفية مرة واحدة للوحة كلها، بأنواع معايير البحث نفسها: نص أو "
                "رقم أو تاريخ أو نعم/لا، أو قائمة خياراتها ثابتة أو تأتي من استعلام. واسمها "
                "(حروف وأرقام وشرطة سفلية) هو ما يظهر في العنوان، وعنوانها الظاهر هو ما يراه "
                "الناس."))
    figure(doc, "29b-dashboard-filters",
           T("The Filters tab: a date range, and a list of regions where you can pick "
             "several, filled from a query.",
             "تبويب «عوامل التصفية»: فترة زمنية، وقائمة مناطق يمكن اختيار أكثر من منطقة فيها، "
             "تأتي من استعلام."))
    para(doc, T("A date filter's starting value can be a date (yyyy-mm-dd) or one that moves "
                "with time: today, week_start, month_start, month_end, last_month_start, "
                "last_month_end, year_start, or today-N and today+N for a number of days. A "
                "From filter set to month_start always opens on the current month, however "
                "long the page has been open.",
                "يمكن أن تكون القيمة الأولى لتصفية التاريخ تاريخًا (yyyy-mm-dd) أو قيمة تتحرك "
                "مع الوقت: today وweek_start وmonth_start وmonth_end وlast_month_start "
                "وlast_month_end وyear_start، أو today-N وtoday+N لعدد من الأيام. فتصفية «من» "
                "المضبوطة على month_start تفتح دائمًا على الشهر الحالي، مهما طال فتح الصفحة."))

    h3(doc, T("Tiles", "العناصر"))
    para(doc, T("The top of the Tiles tab is a small picture of the 12-column grid: drag a "
                "box to move its tile. Below it, each tile names a query and says how to show "
                "it.",
                "أعلى تبويب «العناصر» صورة مصغّرة للشبكة ذات الاثني عشر عمودًا: اسحب أي مربع "
                "لتحريك عنصره. وتحتها يحدد كل عنصر استعلامًا وطريقة عرضه."))
    figure(doc, "29c-dashboard-tiles",
           T("The Tiles tab: the layout picture, then each tile's settings.",
             "تبويب «العناصر»: صورة الترتيب، ثم إعدادات كل عنصر."))
    bullets(doc, [
        (T("Query — ", "الاستعلام — "),
         T("any saved query that reads data. Queries that change data are not offered: a tile "
           "runs again and again, so a change would happen every few minutes for as long as "
           "the page is open.",
           "أي استعلام محفوظ يقرأ البيانات. ولا تظهر الاستعلامات التي تغيّر البيانات: لأن "
           "العنصر يعمل مرة بعد مرة، فكان التغيير سيحدث كل بضع دقائق ما دامت الصفحة مفتوحة.")),
        (T("Width and height — ", "العرض والارتفاع — "),
         T("a quarter, a third, a half, two thirds or the full width, and one to three rows "
           "tall. On a narrow screen the tiles go one under another.",
           "ربع العرض أو ثلثه أو نصفه أو ثلثاه أو العرض كله، وارتفاع من صف إلى ثلاثة صفوف. "
           "وعلى الشاشة الضيقة تظهر العناصر واحدًا تحت الآخر.")),
        (T("Load columns — ", "تحميل الأعمدة — "),
         T("runs the query once to read its column names, so the column settings below become "
           "lists to pick from instead of names to type.",
           "يشغّل الاستعلام مرة واحدة ليقرأ أسماء أعمدته، فتصبح إعدادات الأعمدة أدناه قوائم "
           "تختار منها بدل أسماء تكتبها.")),
        (T("Query parameters — ", "معطيات الاستعلام — "),
         T("each of the query's parameters gets its value from a dashboard filter, from a "
           "fixed value, or from the query's own default. That is how one date range reaches "
           "every tile, whatever name each query gave it.",
           "كل معيار في الاستعلام يأخذ قيمته من عامل تصفية في اللوحة، أو من قيمة ثابتة، أو من "
           "القيمة الافتراضية للاستعلام. وهكذا تصل فترة زمنية واحدة إلى كل العناصر، مهما كان "
           "الاسم الذي أعطاه لها كل استعلام.")),
        (T("Refresh every — ", "التحديث كل — "),
         T("uses a different update time for this tile instead of the dashboard's. Leave it "
           "empty to use the dashboard's.",
           "يجعل لهذا العنصر وقت تحديث مختلفًا عن وقت اللوحة. اتركه فارغًا ليأخذ وقت اللوحة.")),
    ])
    para(doc, T("Show as picks the kind of tile (8.2). A chart tile then has Display options, "
                "and you do not have to set any of them — left alone, the tile looks as it "
                "always did:",
                "«طريقة العرض» تحدد نوع العنصر (٨.٢). ثم يكون لعنصر المخطط «خيارات العرض»، "
                "ولا يلزمك ضبط أي منها — فإن تركتها يظهر العنصر كما كان دائمًا:"))
    table(doc,
          [T("Option", "الخيار"), T("What it does",
                                          "ماذا يفعل")],
          [
              [T("Legend",
                 "وسيلة الإيضاح (دليل الألوان)"),
               T("Automatic, above the chart, below it, or hidden.",
                 "تلقائي، أو فوق المخطط، أو تحته، أو مخفي.")],
              [T("Show values on the chart", "إظهار القيم على المخطط"),
               T("Writes each value on its bar, point or slice.",
                 "يكتب كل قيمة على عمودها أو نقطتها أو شريحتها.")],
              [T("Order", "الترتيب"),
               T("As the query returns them, largest first, smallest first, or by category name.",
                 "كما يعيدها الاستعلام، أو الأكبر أولًا، أو الأصغر أولًا، أو حسب اسم الفئة.")],
              [T("Group the rest as “Other”", "جمع الباقي تحت «أخرى»"),
               T("After the category limit, adds the rest together instead of leaving them "
                 "out, so a pie still shows the whole.",
                 "بعد حد الفئات، يجمع الباقي معًا بدل حذفه، فيبقى المخطط الدائري يُظهر الكل.")],
              [T("Split into series by", "تقسيم إلى سلاسل حسب"),
               T("For rows such as month, region, total: one series for each region. A "
                 "heatmap needs it for its columns.",
                 "لصفوف مثل الشهر والمنطقة والمجموع: سلسلة لكل منطقة. وتحتاجه الخريطة "
                 "الحرارية لأعمدتها.")],
              [T("Show as 100% (shares)", "العرض كنسب (100%)"),
               T("Stacked charts only: each column shows how its total is shared out.",
                 "للمخططات المكدسة فقط: كل عمود يُظهر نسبة كل جزء من مجموعه.")],
              [T("Drawn as a line / Give the line its own scale",
                 "تُرسم كخط / مقياس مستقل للخط"),
               T("Columns + line only: which series are lines, and whether the line gets its "
                 "own scale — only when it is in different units.",
                 "للأعمدة مع الخط فقط: أي السلاسل تُرسم خطوطًا، وهل يكون للخط مقياس خاص — فقط "
                 "عندما تكون وحدته مختلفة.")],
              [T("Axis from / to", "المحور من / إلى"),
               T("Sets the value axis to fixed numbers instead of fitting it to the data.",
                 "يجعل محور القيم بأرقام ثابتة بدل أن يتغير حسب البيانات.")],
              [T("Reference line", "خط مرجعي"),
               T("A dashed line across the chart at a target or a limit, with a label if you "
                 "want one.",
                 "خط متقطع عبر المخطط عند هدف أو حد، مع عنوان إن أردت.")],
              [T("Colours", "الألوان"),
               T("Picks each series' colour from the colour list.",
                 "يختار لون كل سلسلة من قائمة الألوان.")],
              [T("Dial maximum", "الحد الأعلى للمقياس"),
               T("Gauge only. Empty: a round number above the target or the value.",
                 "للمقياس فقط. إن تُرك فارغًا: رقم مُقرَّب أعلى من الهدف أو القيمة.")],
              [T("Totals row, column formats", "صف الإجماليات، تنسيق الأعمدة"),
               T("Table only: a totals row underneath, and how each column's numbers are shown.",
                 "للجدول فقط: صف إجماليات في الأسفل، وطريقة عرض أرقام كل عمود.")],
          ], widths=[2.0, 4.2])
    para(doc, T("On Sales Overview, Sales by region shows the largest first with the values "
                "written on the columns, Staff by department is a doughnut with its colour "
                "key below, and Daily sales is an area chart.",
                "في لوحة أداء المبيعات، يعرض «المبيعات حسب المنطقة» الأكبر أولًا مع كتابة "
                "القيم على الأعمدة، و«الموظفون حسب الإدارة» دائري مجوف ودليل ألوانه تحته، "
                "و«المبيعات اليومية» مخطط مساحة."))
    note(doc, T("A scheduled task saves a dashboard to Word with real Word charts. Word has "
                "no funnel or heatmap chart, so in that file a funnel is drawn as a bar chart "
                "and a heatmap is shown as its table.",
                "المهمة المجدولة تحفظ لوحة المعلومات في Word بمخططات Word حقيقية. وليس في "
                "Word مخطط للقمع ولا للخريطة الحرارية، لذلك يُرسم القمع في ذلك الملف كمخطط "
                "أشرطة، وتظهر الخريطة الحرارية كجدول."))
    para(doc, T("A KPI tile turns the query's rows into one number. Summary says how:",
                "عنصر المؤشر الرقمي يحوّل صفوف الاستعلام إلى رقم واحد، ويحدد «الملخص» كيف:"))
    table(doc,
          [T("Summary", "الملخص"), T("The number shown", "الرقم المعروض"),
           T("Compared with",
             "يُقارن بـ")],
          [
              [T("Last row", "الصف الأخير"),
               T("The value column on the last row. The rows are read in order, oldest first, "
                 "and draw the trend line.",
                 "قيمة العمود في الصف الأخير. وتُقرأ الصفوف بالترتيب، الأقدم أولًا، وترسم خط "
                 "الاتجاه."),
               T("The compare column on that row, or else the row before it.",
                 "عمود المقارنة في ذلك الصف، وإلا فالصف الذي قبله.")],
              [T("Sum, Average, Minimum, Maximum", "المجموع، المتوسط، الأدنى، الأعلى"),
               T("That calculation on the value column over all the rows.",
                 "هذا الحساب على عمود القيمة في كل الصفوف."),
               T("The same calculation on the compare column, if there is one.",
                 "الحساب نفسه على عمود المقارنة، إن وُجد.")],
              [T("Row count", "عدد الصفوف"),
               T("How many rows the query gave back. Needs no value column.",
                 "عدد الصفوف التي أعادها الاستعلام، ولا يحتاج إلى عمود قيمة."),
               T("Nothing, unless a compare column is set.",
                 "لا شيء، ما لم يُحدَّد عمود مقارنة.")],
          ], widths=[1.6, 2.6, 2.0])
    bullets(doc, [
        (T("Last row suits a query that shows a trend. ",
           "«الصف الأخير» يناسب الاستعلام الذي يُظهر اتجاهًا. "),
         T("A query that gives one row per month, oldest first, shows “this month compared "
           "with last month” with a small line for the whole year, and nothing else to set "
           "up.",
           "فالاستعلام الذي يعيد صفًا لكل شهر، الأقدم أولًا، يُظهر «هذا الشهر مقارنةً بالشهر "
           "الماضي» مع خط صغير للسنة كلها، دون أي إعداد آخر.")),
        (T("Higher is better ", "«الأعلى أفضل» "),
         T("decides which direction is green. Untick it for anything that should go down — "
           "complaints, overdue invoices, missing stock.",
           "يحدد أي اتجاه يكون أخضر. أزل التحديد عنه لكل ما يجب أن ينخفض — الشكاوى والفواتير "
           "المتأخرة ونقص المخزون.")),
        (T("Target ", "«الهدف» "),
         T("is optional. Within the Near target range (10% unless changed) the tile says Near "
           "target instead of Off target.",
           "اختياري. وإذا كان الرقم ضمن نسبة «قريب من الهدف» (١٠٪ ما لم تُغيَّر) يكتب العنصر "
           "«قريب من الهدف» بدل «دون الهدف».")),
        (T("Format ", "«التنسيق» "),
         T("shows the number as it is, as a percentage (0.25 is 25%), or as an amount with "
           "two decimal places.",
           "يعرض الرقم كما هو، أو كنسبة مئوية (0.25 تعني ٢٥٪)، أو كمبلغ بخانتين بعد الفاصلة.")),
    ])
    note(doc, T("Use the column names the query gives back, including names changed with AS — "
                "the same rule as report charts. If a query's columns are renamed, the tiles "
                "that use them stop working; the tile then says which column is missing "
                "instead of showing nothing. A query used by a tile cannot be deleted until "
                "the tile is removed.",
                "استخدم أسماء الأعمدة كما يعيدها الاستعلام، بما فيها الأسماء التي غُيّرت بـ "
                "AS — وهي القاعدة نفسها في مخططات التقارير. وإذا تغيّرت أسماء أعمدة الاستعلام "
                "تتوقف العناصر التي تستخدمها، ويذكر العنصر حينها العمود الناقص بدل أن يظهر "
                "فارغًا. ولا يمكن حذف استعلام يستخدمه عنصر حتى يُحذف العنصر."))

    h2(doc, T("8.6 Who can open a dashboard",
              "٨.٦ من يستطيع فتح لوحة المعلومات"))
    para(doc, T("A dashboard is shared the same three ways as a query or a report — by role, "
                "by user group, or with people by name — from Who can view it in the list's "
                "menu. Sales Overview is shared with the Sales Team user group.",
                "تُشارك لوحة المعلومات بالطرق الثلاث نفسها كما في الاستعلام والتقرير — "
                "بالدور، أو بمجموعة المستخدمين، أو مع أشخاص بأسمائهم — من «من يمكنه عرضها» في "
                "قائمة اللوحة. ولوحة أداء المبيعات مشارَكة مع مجموعة «فريق المبيعات»."))
    figure(doc, "29d-dashboard-access",
           T("Dashboard access. Being able to open the dashboard is only the first check.",
             "صلاحية لوحة المعلومات. وفتح اللوحة هو الفحص الأول فقط."))
    bullets(doc, [
        (T("The role must have View dashboards. ",
           "يجب أن يملك الدور صلاحية «عرض لوحات المعلومات». "),
         T("At the start only Admin has it — the User role does not. Add it on Settings → "
           "Permissions before sharing a dashboard with ordinary users, or nobody will be "
           "able to open it.",
           "في البداية لا يملكها إلا Admin — ودور User لا يملكها. أضفها من «الإعدادات ← "
           "الصلاحيات» قبل مشاركة لوحة مع المستخدمين العاديين، وإلا لن يستطيع أحد فتحها.")),
        (T("Every tile is checked by itself. ",
           "كل عنصر يُفحص وحده. "),
         T("Opening a dashboard never gives you the queries on it. Each tile runs only if the "
           "viewer could run its query themselves; if not, it shows a message instead, and "
           "the rest of the dashboard still works.",
           "فتح اللوحة لا يعطيك أبدًا الاستعلامات التي عليها. فلا يعمل العنصر إلا إذا كان "
           "المشاهد يستطيع تشغيل استعلامه بنفسه، وإلا يعرض رسالة بدلًا منه، ويستمر باقي "
           "اللوحة في العمل.")),
    ])

    h2(doc, T("8.7 Moving dashboards to another system", "٨.٧ نقل لوحات المعلومات إلى نظام آخر"))
    para(doc, T("The dashboard list has the same Backup menu as the report list (7.9): export "
                "one dashboard or all of them, and import from a file. You need “Export and "
                "import dashboard definitions”. Include the queries and reports they use also "
                "saves the tiles' queries, the queries behind the filter lists, and the "
                "reports a tile opens when clicked — and because the file then contains them, "
                "you also need the query and report export permissions.",
                "لصفحة لوحات المعلومات قائمة «النسخ الاحتياطي» نفسها التي في صفحة التقارير "
                "(٧.٩): تصدير لوحة واحدة أو كل اللوحات، والاستيراد من ملف. وتحتاج إلى صلاحية "
                "«تصدير تعريفات لوحات المعلومات واستيرادها». وخيار «تضمين الاستعلامات "
                "والتقارير التي تستخدمها» يحفظ أيضًا استعلامات العناصر، والاستعلامات التي "
                "تأتي منها قوائم التصفية، والتقارير التي يفتحها العنصر عند النقر — ولأن الملف "
                "يحتويها حينها، تحتاج أيضًا إلى صلاحيتَي تصدير الاستعلامات والتقارير."))
    para(doc, T("On import, anything that cannot be found becomes a warning, not an error: a "
                "tile whose query is missing is left out, clicking through to a missing "
                "report is turned off, a filter whose list query is missing becomes a text "
                "box, and access for a role, group or user that does not exist here is left "
                "out.",
                "عند الاستيراد، ما لم يُعثر عليه يصبح ملاحظة وليس خطأ: فالعنصر الذي ينقصه "
                "استعلامه يُترك، والنقر إلى تقرير غير موجود يُوقف، وعامل التصفية التي ينقصها "
                "استعلام قائمتها تصبح مربع نص، والصلاحية لدور أو مجموعة أو مستخدم غير موجود "
                "هنا تُترك."))

    page_break(doc)

    # ===================================================================== 9
    h1(doc, T("9. Query groups", "٩. مجموعات الاستعلامات"))
    para(doc, T("For: Administrators create and edit them; Access Managers decide who can use "
                "them.",
                "لمن: ينشئها المسؤولون ويعدّلونها، ويحدد مديرو الصلاحيات من يستطيع استخدامها."),
         italic=True)
    para(doc, T("A query group is a folder. It keeps My Queries tidy for users, and — more "
                "importantly — it is how an Access Manager gives access: giving someone a "
                "group gives them every query in it.",
                "مجموعة الاستعلامات مجلد. ترتّب صفحة «الاستعلامات» للمستخدمين، والأهم أنها "
                "الطريقة التي يعطي بها مدير الصلاحيات الوصول: فإعطاء المجموعة لشخص يعطيه كل "
                "استعلام فيها."))
    figure(doc, "20-query-groups",
           T("Query Groups. The Queries column counts the queries in each group; click it to "
             "see the list of just that group's queries.",
             "مجموعات الاستعلامات. عمود «الاستعلامات» يعدّ ما في كل مجموعة، وانقر عليه لترى "
             "استعلامات تلك المجموعة فقط."))
    figure(doc, "21-query-group-create",
           T("Creating a group: a name and a description.", "إنشاء مجموعة: اسم ووصف."))
    para(doc, T("A query can be in several groups at once — choose them in the query's Groups "
                "field (6.2). It then appears in each folder on My Queries, and is counted in "
                "each group's Queries column. A shared list, or a report that both Finance "
                "and Customer Service need, can sit in both teams' groups instead of being "
                "given twice.",
                "يمكن أن يكون الاستعلام في عدة مجموعات معًا — اخترها في حقل «المجموعات» في "
                "الاستعلام (٦.٢). فيظهر حينها في كل مجلد منها في صفحة «الاستعلامات»، ويُحسب "
                "في عمود «الاستعلامات» لكل مجموعة. فالقائمة المشتركة، أو التقرير الذي تحتاجه "
                "«المالية» و«خدمة العملاء» معًا، يمكن وضعه في مجموعتي الفريقين بدل إعطائه "
                "مرتين."))
    note(doc, T("Deleting a group does not delete its queries. They stay in any other groups "
                "they are in, and a query that was only in this group becomes ungrouped.",
                "حذف المجموعة لا يحذف استعلاماتها. فهي تبقى في أي مجموعات أخرى هي فيها، "
                "والاستعلام الذي كان في هذه المجموعة فقط يصبح غير مُصنَّف."))

    page_break(doc)

    # ===================================================================== 10
    h1(doc, T("10. Giving access",
              "١٠. إعطاء الصلاحيات"))
    para(doc, T("For: Administrators, and Access Managers.",
                "لمن: المسؤولون ومديرو الصلاحيات."), italic=True)

    h2(doc, T("10.1 Access to one query",
              "١٠.١ صلاحية استعلام واحد"))
    para(doc, T("The shield icon on a query row opens Manage Query Access, which has three "
                "tabs. Each tab is saved by itself — the button under a tab saves only that "
                "tab.",
                "أيقونة الدرع في صف الاستعلام تفتح صفحة «إدارة صلاحيات الاستعلام»، وفيها "
                "ثلاثة تبويبات. وكل تبويب يُحفظ وحده — فالزر تحت التبويب يحفظ ذلك التبويب "
                "فقط."))
    figure(doc, "15-query-access-roles",
           T("The Roles tab. Auditor and AccessManager are left out on purpose: neither can "
             "be given a query.",
             "تبويب الأدوار. ولا يظهر Auditor وAccessManager عمدًا: فلا يمكن إعطاء أي منهما "
             "استعلامًا."))
    figure(doc, "16-query-access-user-groups",
           T("The User Groups tab. Everyone in a group listed here gets the query, and the "
             "number of people is shown next to each group.",
             "تبويب مجموعات المستخدمين. كل من في مجموعة مذكورة هنا يحصل على الاستعلام، ويظهر "
             "عدد الأشخاص بجانب كل مجموعة."))
    figure(doc, "17-query-access-users",
           T("The Users tab, for giving the query to people by name.",
             "تبويب المستخدمين، لإعطاء الاستعلام لأشخاص بأسمائهم."))

    h2(doc, T("10.2 Access to a group", "١٠.٢ صلاحية المجموعة"))
    para(doc, T("Manage Group Access works the same way, with the same three tabs, and "
                "applies to every query in the group — including one that is also in other "
                "groups. This is the page an Access Manager uses most.",
                "صفحة «إدارة صلاحيات المجموعة» تعمل بالطريقة نفسها وبالتبويبات الثلاثة نفسها، "
                "وتشمل كل استعلام في المجموعة — حتى الاستعلام الموجود في مجموعات أخرى أيضًا. "
                "وهي الصفحة التي يستخدمها مدير الصلاحيات أكثر من غيرها."))
    figure(doc, "22-query-group-access",
           T("Manage Group Access.", "إدارة صلاحيات المجموعة."))

    h2(doc, T("10.3 How access adds up",
              "١٠.٣ كيف تُجمع الصلاحيات"))
    para(doc, T("Access adds up, and there is no “deny”. A user can use a query if any one of "
                "these is true: a role they have was given the query or any group it is in; a "
                "user group they are in was given it; they were given it by name; or they are "
                "an administrator. Removing one of these leaves the others in place — to "
                "remove access completely, check all three tabs on the query and on every "
                "group it is in, and who is in any user group that was given it.",
                "الصلاحيات تُجمع، ولا يوجد «منع». فيستطيع المستخدم استخدام الاستعلام إذا تحقق "
                "أي مما يلي: أن يكون دور له قد أُعطي الاستعلام أو أي مجموعة هو فيها، أو أن "
                "تكون مجموعة مستخدمين هو فيها قد أُعطيته، أو أن يكون قد أُعطيه باسمه، أو أن "
                "يكون مسؤولًا. وإزالة واحدة منها تترك الباقي كما هو — ولإزالة الصلاحية تمامًا "
                "افحص التبويبات الثلاثة في الاستعلام وفي كل مجموعة هو فيها، وافحص من في أي "
                "مجموعة مستخدمين أُعطيته."))

    page_break(doc)

    # ===================================================================== 11
    h1(doc, T("11. Scheduled tasks", "١١. المهام المجدولة"))
    para(doc, T("For: Administrators create and run them; Auditors can look at them; people "
                "added as viewers see how they went.",
                "لمن: ينشئها المسؤولون ويشغّلونها، ويطّلع عليها المدققون، ويرى المشاهدون "
                "المضافون نتيجة تشغيلها."), italic=True)
    para(doc, T("A scheduled task runs one or more queries by itself at set times and saves "
                "the results to files on the server.",
                "المهمة المجدولة تشغّل استعلامًا أو أكثر تلقائيًا في أوقات محددة، وتحفظ "
                "النتائج في ملفات على الخادم."))

    h2(doc, T("11.1 The task list", "١١.١ قائمة المهام"))
    figure(doc, "30-scheduled-tasks",
           T("Scheduled Tasks, with the next and last run time for each task.",
             "المهام المجدولة، مع وقت التشغيل القادم والأخير لكل مهمة."))
    bullets(doc, [
        (T("Run now — ", "تشغيل الآن — "),
         T("starts the task straight away, without waiting for its time.",
           "يبدأ المهمة فورًا دون انتظار موعدها.")),
        (T("Run history — ", "سجل التشغيل — "),
         T("opens the list of past runs.",
           "يفتح قائمة مرات التشغيل السابقة.")),
        (T("Edit / Delete — ", "تعديل/حذف — "),
         T("administrators only. Deleting a task also deletes its run history.",
           "للمسؤولين فقط. وحذف المهمة يحذف سجل تشغيلها أيضًا.")),
        (T("Enabled — ", "مفعَّلة — "),
         T("a disabled task keeps its settings, but it does not run.",
           "المهمة المعطّلة تحتفظ بإعداداتها، لكنها لا تعمل.")),
    ])

    h2(doc, T("11.2 Creating a task", "١١.٢ إنشاء مهمة"))
    figure(doc, "31-scheduled-task-create",
           T("The Create Scheduled Task form.", "نموذج إنشاء مهمة مجدولة."))
    h3(doc, T("Task", "المهمة"))
    table(doc,
          [T("Field", "الحقل"), T("Meaning", "المعنى")],
          [
              [T("Name", "الاسم"), T("The task's name. Required.",
                                          "اسم المهمة. مطلوب.")],
              [T("Output folder",
                 "مجلد الإخراج"),
               T("The full folder path on the server where files are saved. It is created if "
                 "it does not exist. Required.",
                 "المسار الكامل للمجلد على الخادم الذي تُحفظ فيه الملفات. ويُنشأ إن لم يكن "
                 "موجودًا. مطلوب.")],
              [T("Archive folder", "مجلد الأرشيف"),
               T("Optional. A copy of every file is also saved here.",
                 "اختياري. وتُحفظ فيه نسخة من كل ملف أيضًا.")],
              [T("Combine all results into one file", "دمج كل النتائج في ملف واحد"),
               T("Puts every query's result into one file, in query order.",
                 "يضع نتيجة كل استعلام في ملف واحد، بترتيب الاستعلامات.")],
              [T("Include header row",
                 "تضمين صف الرؤوس"),
               T("Off means CSV and Excel files have data rows only, with no column titles.",
                 "إذا كان مغلقًا تحتوي ملفات CSV وExcel على صفوف البيانات فقط، دون عناوين "
                 "الأعمدة.")],
              [T("Timestamp format",
                 "تنسيق الطابع الزمني"),
               T("A date pattern added to file names, for example -yyyy-MM-dd. Without it, "
                 "the same file is replaced on every run.",
                 "شكل التاريخ الذي يُضاف إلى أسماء الملفات، مثل ‎-yyyy-MM-dd‎. وبدونه يُستبدل "
                 "الملف نفسه في كل تشغيل.")],
          ],
          widths=[1.9, 4.7])

    h3(doc, T("Schedule", "الجدولة"))
    para(doc, T("A task can have several run times and runs at each of them — for example "
                "every day at 03:00 and every month on day 14. Each run time is one of these:",
                "يمكن أن يكون للمهمة عدة مواعيد وتعمل في كل منها — مثلًا يوميًا الساعة ٠٣:٠٠ "
                "وشهريًا في اليوم ١٤. وكل موعد من هذه الأنواع:"))
    table(doc,
          [T("How often",
             "كم مرة"), T("Also set",
                                               "تحدد أيضًا")],
          [
              [T("Every N minutes", "كل N دقيقة"), T("Number of minutes between runs",
                                                            "عدد الدقائق بين كل تشغيل")],
              [T("Daily", "يوميًا"), T("Time of day",
                                             "الساعة")],
              [T("Weekly", "أسبوعيًا"), T("Day of the week and time",
                                                  "يوم الأسبوع والساعة")],
              [T("Monthly", "شهريًا"), T("Day of the month and time",
                                               "يوم الشهر والساعة")],
          ],
          widths=[1.9, 4.7])

    h3(doc, T("Queries to export", "الاستعلامات المراد تصديرها"))
    para(doc, T("Add one row for each query. For a query that reads data you also choose:",
                "أضف صفًا لكل استعلام. وللاستعلام الذي يقرأ البيانات تختار أيضًا:"))
    bullets(doc, [
        (T("Format — ", "الصيغة — "),
         T("the type of file for that query (with a choice of separator for CSV).",
           "نوع ملف ذلك الاستعلام (مع اختيار الفاصل في CSV).")),
        (T("File name — ", "اسم الملف — "),
         T("the query's name, unless you change it.",
           "اسم الاستعلام، ما لم تغيّره.")),
        (T("Append timestamp — ", "إلحاق طابع زمني — "),
         T("on keeps a separate file for each run; off replaces the same file each time.",
           "عند تفعيله يُحفظ ملف منفصل لكل تشغيل، وعند إغلاقه يُستبدل الملف نفسه في كل مرة.")),
        (T("Incremental run — ",
           "التشغيل الإضافي (الجديد فقط) — "),
         T("optional. Choose a number column, the parameter that receives it, and a starting "
           "value: each run passes the highest number from the last run into that parameter, "
           "so only new rows are exported. Sort the query by that column, smallest first.",
           "اختياري. اختر عمودًا رقميًا، والمعيار الذي يستقبله، وقيمة البداية: كل تشغيل يمرّر "
           "أكبر رقم من التشغيل السابق إلى هذا المعيار، فتُصدَّر الصفوف الجديدة فقط. ورتّب "
           "الاستعلام حسب هذا العمود من الأصغر إلى الأكبر.")),
        (T("Parameter values — ", "قيم معايير البحث — "),
         T("fixed values for the query's parameters, because nobody is there to type them.",
           "قيم ثابتة لمعايير البحث في الاستعلام، لأنه لا يوجد أحد ليكتبها وقت التشغيل.")),
    ])
    note(doc, T("A query that changes data can be scheduled too. It makes its change for real "
                "on every run — there is no preview and no file, only the number of changed "
                "rows in the run status.",
                "يمكن جدولة استعلام يغيّر البيانات أيضًا. وهو ينفّذ التغيير فعلًا في كل تشغيل "
                "— دون معاينة ودون ملف، وإنما يظهر عدد الصفوف التي تغيّرت في حالة التشغيل "
                "فقط."),
        kind="warning")
    figure(doc, "32-scheduled-task-edit",
           T("An existing task, showing its run times, its queries and their parameter "
             "values.",
             "مهمة موجودة، تظهر فيها مواعيدها واستعلاماتها وقيم معايير البحث."))

    h2(doc, T("11.3 Who can see the task",
              "١١.٣ من يرى المهمة"))
    para(doc, T("Who can see a task is set on its own page — open it with the shield button "
                "in the task list, not from the task form. So changing the times never "
                "changes who can see it. The page gives two things, in the same three ways as "
                "query access: to a role, to a user group, or to a person by name.",
                "من يرى المهمة يُحدَّد في صفحة خاصة — تفتحها بزر الدرع في قائمة المهام، وليس "
                "من نموذج المهمة. لذلك لا يغيّر تعديل المواعيد أبدًا من يراها. وتعطي الصفحة "
                "شيئين، بالطرق الثلاث نفسها كما في صلاحيات الاستعلامات: لدور، أو لمجموعة "
                "مستخدمين، أو لشخص باسمه."))
    bullets(doc, [
        (T("Viewing — ",
           "المشاهدة — "),
         T("the task appears on that person's Schedules page, with its runs, whether they "
           "worked, and the names of the files each run made.",
           "تظهر المهمة في صفحة «المهام المجدولة» لهذا الشخص، مع مرات تشغيلها، وهل نجحت، "
           "وأسماء الملفات التي صنعها كل تشغيل.")),
        (T("Downloading — ", "التنزيل — "),
         T("a smaller permission, chosen from the viewers on the same tab. Without it a "
           "viewer can see that a file was made but cannot download it.",
           "صلاحية أصغر، تُختار من المشاهدين في التبويب نفسه. وبدونها يرى المشاهد أن ملفًا "
           "صُنع لكنه لا يستطيع تنزيله.")),
    ])
    figure(doc, "34-scheduled-task-access-roles",
           T("Manage Task Access, Roles tab. Everyone with a listed role sees the task; the "
             "second list chooses which of those roles can also download the files.",
             "«إدارة الوصول إلى المهمة»، تبويب الأدوار. كل من له دور مذكور هنا يرى المهمة، "
             "والقائمة الثانية تحدد أي هذه الأدوار يستطيع أيضًا تنزيل الملفات."))
    figure(doc, "35-scheduled-task-access-user-groups",
           T("The User Groups tab. Who is in the group is checked each time, so adding "
             "someone to the group gives them the task straight away, and removing them takes "
             "it away straight away.",
             "تبويب مجموعات المستخدمين. يُفحص من في المجموعة في كل مرة، فإضافة شخص إلى "
             "المجموعة تعطيه المهمة فورًا، وإخراجه منها يسحبها منه فورًا."))
    figure(doc, "36-scheduled-task-access-users",
           T("The Users tab, for giving one person the task without giving it to their whole "
             "role or group.",
             "تبويب المستخدمين، لإعطاء المهمة لشخص واحد دون إعطائها لدوره أو مجموعته كلها."))
    para(doc, T("Each tab is saved by itself and changes only its own list, so two "
                "administrators working on different tabs cannot undo each other's work. "
                "Access adds up: someone who gets it two ways keeps the bigger one, so to "
                "take away downloading you must remove it everywhere it was given. "
                "Administrators and Auditors see every task anyway; but downloading always "
                "needs either the general download permission or one of these — which is why "
                "an Auditor sees the run history but not the files.",
                "كل تبويب يُحفظ وحده ويغيّر قائمته فقط، فلا يستطيع مسؤولان يعملان على تبويبين "
                "مختلفين أن يُلغي أحدهما عمل الآخر. والصلاحيات تُجمع: من حصل عليها بطريقتين "
                "يحتفظ بالأكبر، فلسحب التنزيل يجب إزالته من كل مكان أُعطي فيه. ويرى المسؤولون "
                "والمدققون كل المهام على أي حال، لكن التنزيل يحتاج دائمًا إما صلاحية التنزيل "
                "العامة أو واحدة من هذه — ولهذا يرى المدقق سجل التشغيل دون الملفات."))

    h2(doc, T("11.4 Run history", "١١.٤ سجل التشغيل"))
    figure(doc, "33-scheduled-task-runs",
           T("Run history. Open a run to see the queries it ran, the files it saved, the "
             "number of rows, how long each took and any error.",
             "سجل التشغيل. افتح أي تشغيل لترى الاستعلامات التي شغّلها، والملفات التي حفظها، "
             "وعدد الصفوف، والمدة، وأي خطأ."))
    bullets(doc, [
        (T("Download — ", "تنزيل — "),
         T("downloads a file the run made. If the file was moved, deleted or replaced by a "
           "later run, you are told so instead.",
           "ينزّل ملفًا صنعه التشغيل. وإذا كان الملف قد نُقل أو حُذف أو استُبدل بتشغيل لاحق، "
           "يخبرك بذلك.")),
        (T("Cancel — ", "إلغاء — "),
         T("appears on a run that is still going, and stops the query on the database. "
           "Administrators only.",
           "يظهر على التشغيل الذي لم ينتهِ بعد، ويوقف الاستعلام على قاعدة البيانات. للمسؤولين "
           "فقط.")),
        (T("Checkpoint — ", "نقطة التوقف — "),
         T("for a query that exports only new rows, the number the run stopped at; the next "
           "run starts from there.",
           "في الاستعلام الذي يصدّر الصفوف الجديدة فقط، هو الرقم الذي توقف عنده التشغيل، "
           "ويبدأ منه التشغيل التالي.")),
    ])
    note(doc, T("Only one run can be open at a time — opening another closes the first.",
                "لا يُفتح إلا تشغيل واحد في كل مرة — وفتح آخر يغلق الأول."))

    page_break(doc)

    # ===================================================================== 12
    h1(doc, T("12. Users, user groups and Active Directory",
              "١٢. المستخدمون ومجموعات المستخدمين وActive Directory"))
    para(doc, T("For: Administrators. Access Managers can use the Users page with some "
                "limits.",
                "لمن: المسؤولون. ويستطيع مديرو الصلاحيات استخدام صفحة المستخدمين مع بعض "
                "القيود."),
         italic=True)

    h2(doc, T("12.1 User management", "١٢.١ إدارة المستخدمين"))
    figure(doc, "51-user-management",
           T("User Management. The list shows each account's roles, whether it was made here "
             "or came from Active Directory, and whether it is active.",
             "إدارة المستخدمين. تعرض القائمة أدوار كل حساب، وهل أُنشئ هنا أم جاء من Active "
             "Directory، وهل هو نشط."))
    figure(doc, "52-user-create-form",
           T("Creating a user in the system. Email is optional. Permissions come from the "
             "roles chosen here and from any user group the account is added to later.",
             "إنشاء مستخدم في النظام. البريد الإلكتروني اختياري. وتأتي الصلاحيات من الأدوار "
             "المختارة هنا ومن أي مجموعة مستخدمين يُضاف إليها الحساب لاحقًا."))
    table(doc,
          [T("Button",
             "الزر"), T("What it does",
                                            "ماذا يفعل")],
          [
              [T("Create User", "إنشاء مستخدم"),
               T("Adds an account with a password and roles.",
                 "يضيف حسابًا بكلمة مرور وأدوار.")],
              [T("Edit", "تعديل"),
               T("Changes the username, the roles, or both.",
                 "يغيّر اسم المستخدم أو الأدوار أو كليهما.")],
              [T("Reset password", "إعادة تعيين كلمة المرور"),
               T("Gives the account a new temporary password, which you must pass on to the "
                 "person. The old password stops working straight away.",
                 "يعطي الحساب كلمة مرور مؤقتة جديدة، وعليك أن توصلها للشخص. وتتوقف كلمة "
                 "المرور القديمة فورًا.")],
              [T("Deactivate / Activate", "تعطيل/تفعيل"),
               T("A deactivated account is signed out and cannot sign in until it is "
                 "activated again.",
                 "الحساب المعطّل يُخرَج من النظام ولا يستطيع الدخول حتى يُفعَّل من جديد.")],
          ],
          widths=[1.7, 4.9])
    note(doc, T("An Access Manager can use this page but cannot change any account that has "
                "the Admin role, cannot give the Admin role, and cannot change their own "
                "roles. If they try, it is refused, and the attempt is recorded in the system "
                "audit.",
                "يستطيع مدير الصلاحيات استخدام هذه الصفحة، لكنه لا يستطيع تعديل أي حساب له "
                "دور Admin، ولا إعطاء دور Admin، ولا تغيير أدواره هو. وإذا حاول يُرفض ذلك، "
                "وتُسجَّل المحاولة في سجل عمليات النظام."),
        kind="warning")

    h2(doc, T("12.2 User groups", "١٢.٢ مجموعات المستخدمين"))
    para(doc, T("A user group is a named list of people, kept in this system. Giving a query "
                "or a query group to a user group gives it to everyone in it — so one change "
                "gives a whole team access, and one change takes it away again.",
                "مجموعة المستخدمين قائمة أشخاص لها اسم، محفوظة في هذا النظام. وإعطاء استعلام "
                "أو مجموعة استعلامات لمجموعة مستخدمين يعطيه لكل من فيها — فبتغيير واحد يحصل "
                "الفريق كله على الوصول، وبتغيير واحد يُسحب منه."))
    note(doc, T("These groups are not Active Directory groups and do not copy the AD "
                "department. Who is in a group is exactly what an administrator set here, so "
                "it can include accounts made here as well as imported ones, and changes in "
                "Active Directory never quietly change who can use a query.",
                "هذه المجموعات ليست مجموعات Active Directory ولا تنسخ القسم الموجود هناك. ومن "
                "في المجموعة هو بالضبط ما حدده المسؤول هنا، فيمكن أن تضم حسابات أُنشئت هنا "
                "وحسابات مستوردة معًا، والتغييرات في Active Directory لا تغيّر أبدًا من دون "
                "علمك من يستطيع استخدام الاستعلامات."))
    figure(doc, "52a-user-groups",
           T("User Groups. The Members column is how many people get access when you give "
             "something to that group; the filter searches member names too.",
             "مجموعات المستخدمين. عمود الأعضاء هو عدد الأشخاص الذين يحصلون على الوصول عندما "
             "تعطي شيئًا لتلك المجموعة، والبحث يشمل أسماء الأعضاء أيضًا."))
    figure(doc, "52b-user-group-create",
           T("Creating a group. Filter the list of accounts, then tick the members; the box "
             "shows how many are chosen.",
             "إنشاء مجموعة. صفِّ قائمة الحسابات ثم حدّد الأعضاء، ويعرض المربع عدد من اخترتهم."))
    table(doc,
          [T("Button",
             "الزر"), T("What it does",
                                            "ماذا يفعل")],
          [
              [T("Create Group", "إنشاء مجموعة"),
               T("Adds a group with a name, a description and its first members. Two groups "
                 "cannot have the same name.",
                 "يضيف مجموعة باسم ووصف وأعضائها الأوائل. ولا يمكن أن يكون لمجموعتين الاسم "
                 "نفسه.")],
              [T("Edit", "تعديل"),
               T("Renames the group or changes who is in it. Removing someone takes away "
                 "every query the group gave them, from their very next click — they do not "
                 "need to sign out and in again.",
                 "يغيّر اسم المجموعة أو من فيها. وإخراج شخص منها يسحب منه كل استعلام أعطته له "
                 "المجموعة من نقرته التالية — دون حاجة إلى الخروج والدخول من جديد.")],
              [T("Delete", "حذف"),
               T("Deletes the group and all the access given to it. The members' accounts are "
                 "not touched.",
                 "يحذف المجموعة وكل الصلاحيات التي أُعطيت لها. ولا تتأثر حسابات الأعضاء.")],
          ],
          widths=[1.7, 4.9])
    para(doc, T("Administrators and Access Managers both use this page: deciding who is in a "
                "group is the same job as deciding what the group can use. Query groups are "
                "something else — only an administrator creates, edits or deletes those.",
                "يستخدم هذه الصفحة المسؤولون ومديرو الصلاحيات معًا: فتحديد من في المجموعة هو "
                "العمل نفسه الذي يحدد ما تستطيع المجموعة استخدامه. أما مجموعات الاستعلامات "
                "فشيء آخر — لا ينشئها أو يعدّلها أو يحذفها إلا المسؤول."))
    note(doc, T("Nobody can add themselves to a group — the option is greyed out, and the "
                "server refuses it. Otherwise an Access Manager could give a query to a group "
                "and then join it, which is giving themselves access. An administrator can "
                "add them; after that they can save the group, or leave it, without being "
                "blocked.",
                "لا يستطيع أحد أن يضيف نفسه إلى مجموعة — فالخيار معطّل، والخادم يرفض ذلك. "
                "وإلا لاستطاع مدير الصلاحيات أن يعطي استعلامًا لمجموعة ثم ينضم إليها، فيكون "
                "قد أعطى نفسه الصلاحية. ويستطيع المسؤول إضافته، وبعدها يستطيع حفظ المجموعة أو "
                "الخروج منها دون منع."),
        kind="warning")

    h2(doc, T("12.3 Active Directory", "١٢.٣ Active Directory"))
    para(doc, T("AD Users brings accounts in from Active Directory so they can be given "
                "queries — directly, or by putting them in a user group. It has three tabs.",
                "صفحة «مستخدمو Active Directory» تُدخل الحسابات من Active Directory حتى يمكن "
                "إعطاؤها استعلامات — مباشرة أو بوضعها في مجموعة مستخدمين. وفيها ثلاثة "
                "تبويبات."))
    figure(doc, "55-ad-users-search",
           T("Search Users. Type at least two letters, tick the accounts to bring in, then "
             "press Import Selected Users. Accounts already brought in cannot be picked "
             "again.",
             "بحث المستخدمين. اكتب حرفين على الأقل، وحدّد الحسابات التي تريد إدخالها، ثم اضغط "
             "«استيراد المستخدمين المحددين». ولا يمكن اختيار الحسابات التي أُدخلت من قبل مرة "
             "أخرى."))
    figure(doc, "56-ad-users-departments",
           T("Departments. See the people in an Active Directory department, or bring them "
             "all in at once. This is the only page that reads AD departments — they bring "
             "accounts in, but they do not give any access.",
             "الأقسام. اعرض الأشخاص في قسم من Active Directory، أو أدخلهم جميعًا مرة واحدة. "
             "وهذه هي الصفحة الوحيدة التي تقرأ أقسام Active Directory — فهي تُدخل الحسابات، "
             "لكنها لا تعطي أي صلاحية."))
    figure(doc, "57-ad-users-imported",
           T("Imported Users — the accounts already in the system, with Sync from AD to "
             "update their details.",
             "المستخدمون المستوردون — الحسابات الموجودة في النظام، مع «مزامنة من AD» لتحديث "
             "بياناتهم."))
    bullets(doc, [
        (T("Sync from AD — ", "مزامنة من AD — "),
         T("updates names, emails and departments for accounts already brought in. It does "
           "not change group membership or anyone's access.",
           "يحدّث الأسماء والبريد الإلكتروني والأقسام للحسابات الموجودة. ولا يغيّر من في "
           "المجموعات ولا صلاحيات أحد.")),
        (T("Revoke access — ", "سحب الوصول — "),
         T("an imported user loses access to the system straight away. You can undo it from "
           "the same page.",
           "يفقد المستخدم المستورد الوصول إلى النظام فورًا. ويمكنك التراجع من الصفحة نفسها.")),
        (T("Restore access — ", "استعادة الوصول — "),
         T("gives a removed account its access back.",
           "يعيد الوصول إلى الحساب الذي سُحب منه.")),
    ])

    page_break(doc)

    # ===================================================================== 13
    h1(doc, T("13. Database connections", "١٣. اتصالات قواعد البيانات"))
    para(doc, T("For: Administrators only.", "لمن: المسؤولون فقط."), italic=True)
    para(doc, T("A database connection — shown as a “DB User” — is the database login a query "
                "runs through. Having several lets different queries use different databases, "
                "or the same database with different rights.",
                "اتصال قاعدة البيانات — ويظهر باسم «مستخدم قاعدة البيانات» — هو اسم المستخدم "
                "وكلمة المرور اللذان يعمل الاستعلام من خلالهما. ووجود أكثر من اتصال يسمح "
                "لاستعلامات مختلفة باستخدام قواعد بيانات مختلفة، أو القاعدة نفسها بصلاحيات "
                "مختلفة."))
    figure(doc, "53-database-users",
           T("The database connections list.", "قائمة اتصالات قواعد البيانات."))
    figure(doc, "54-database-user-form",
           T("Creating a connection. The boxes shown depend on the database type.",
             "إنشاء اتصال. والخانات الظاهرة تعتمد على نوع قاعدة البيانات."))
    table(doc,
          [T("Field", "الحقل"), T("Meaning", "المعنى")],
          [
              [T("Name", "الاسم"),
               T("An easy name, e.g. “Production read-only”. Queries use this name, and it is "
                 "the name saved in an export file.",
                 "اسم سهل، مثل «الإنتاج للقراءة فقط». والاستعلامات تستخدم هذا الاسم، وهو "
                 "الاسم الذي يُحفظ في ملف التصدير.")],
              [T("Database Type", "نوع قاعدة البيانات"),
               "Oracle، SQL Server، MySQL، PostgreSQL" if is_rtl()
               else "Oracle, SQL Server, MySQL or PostgreSQL."],
              [T("Host / Port",
                 "الخادم/المنفذ"),
               T("Where the database server is.",
                 "مكان خادم قاعدة البيانات.")],
              [T("Service Name / Database Name", "اسم الخدمة/اسم قاعدة البيانات"),
               T("Which database — the box shown depends on the type.",
                 "أي قاعدة بيانات — والخانة الظاهرة تعتمد على النوع.")],
              [T("DB Username / Password", "اسم المستخدم/كلمة المرور"),
               T("The login. The password is stored encrypted and, when editing, stays the "
                 "same if you leave the box empty.",
                 "بيانات الدخول. كلمة المرور محفوظة مشفّرة، وعند التعديل تبقى كما هي إذا تركت "
                 "الخانة فارغة.")],
          ],
          widths=[2.1, 4.5])
    bullets(doc, [
        (T("Test Connection — ", "اختبار الاتصال — "),
         T("checks the settings with the real server before you save.",
           "يجرّب الإعدادات مع الخادم الحقيقي قبل الحفظ.")),
        (T("Manage Access — ", "إدارة الصلاحيات — "),
         T("chooses which roles can run queries that use this connection. A user needs access "
           "to both the query and its connection.",
           "يحدد الأدوار التي تستطيع تشغيل الاستعلامات التي تستخدم هذا الاتصال. ويحتاج "
           "المستخدم إلى صلاحية على الاستعلام وعلى اتصاله معًا.")),
        (T("Copy — ", "نسخ — "),
         T("creates a new connection already filled in from this one.",
           "ينشئ اتصالًا جديدًا مملوءًا من هذا الاتصال.")),
        (T("Delete — ", "حذف — "),
         T("any query using the connection goes back to the system's default connection.",
           "أي استعلام يستخدم الاتصال يعود إلى اتصال النظام الافتراضي.")),
    ])

    page_break(doc)

    # ===================================================================== 14
    h1(doc, T("14. Logs",
              "١٤. السجلات"))
    para(doc, T("For: Administrators and Auditors.", "لمن: المسؤولون والمدققون."), italic=True)
    para(doc, T("There are two separate logs, and the difference matters: the execution logs "
                "show what people ran, and the system audit shows what people changed.",
                "هناك سجلّان منفصلان، والفرق بينهما مهم: سجلات التنفيذ تبيّن ماذا شغّل الناس، "
                "وسجل عمليات النظام يبيّن ماذا غيّر الناس."))

    h2(doc, T("14.1 Execution logs", "١٤.١ سجلات التنفيذ"))
    para(doc, T("Every run adds a row — whether it worked, failed or was only a preview. "
                "Filtering, sorting and paging happen on the server, so the list stays fast "
                "even when it is very long.",
                "كل تشغيل يضيف صفًا — سواء نجح أو فشل أو كان معاينة فقط. والتصفية والترتيب "
                "والتنقل بين الصفحات تتم على الخادم، فتبقى القائمة سريعة حتى لو كانت طويلة "
                "جدًا."))
    figure(doc, "40-execution-logs",
           T("Execution Logs. Each row shows the query, its type, who ran it, the parameters, "
             "when, how long it took, how many rows, and whether it worked.",
             "سجلات التنفيذ. يعرض كل صف الاستعلام ونوعه ومن شغّله ومعايير البحث ومتى ومدته "
             "وعدد الصفوف وهل نجح."))
    bullets(doc, [
        (T("Search — ", "البحث — "),
         T("looks in the query name, user, parameters and error text.",
           "يبحث في اسم الاستعلام والمستخدم ومعايير البحث ونص الخطأ.")),
        (T("Status — ", "الحالة — "),
         T("runs that worked or runs that failed.",
           "مرات التشغيل الناجحة أو الفاشلة.")),
        (T("Type — ", "النوع — "),
         T("SELECT, INSERT, UPDATE, DELETE or Other.",
           "SELECT أو INSERT أو UPDATE أو DELETE أو أخرى.")),
        (T("Parameters — ", "معايير البحث — "),
         T("shown for every run, whatever kind of query it is — so a DELETE shows exactly "
           "which values picked the rows it removed, and an INSERT shows what it added. Each "
           "one is a small “name = value” label; a list parameter shows all its values; point "
           "at them to see them all.",
           "تظهر لكل تشغيل أيًا كان نوع الاستعلام — فيُظهر DELETE بالضبط القيم التي اختارت "
           "الصفوف التي حذفها، ويُظهر INSERT ما أضافه. وكل معيار بطاقة صغيرة «الاسم = "
           "القيمة»، والمعيار الذي فيه قائمة قيم يُظهرها كلها، وضع المؤشر فوقها لتراها كلها.")),
        (T("Rows before — ", "الصفوف قبل التغيير — "),
         T("for an UPDATE or DELETE run, opens the changed rows as they were before. It only "
           "appears if the query had “Save before-change values” turned on at that time.",
           "في UPDATE وDELETE يفتح الصفوف التي تغيّرت كما كانت قبل التغيير. ولا يظهر إلا إذا "
           "كان خيار «حفظ القيم قبل التغيير» مفعّلًا في ذلك الوقت.")),
    ])
    para(doc, T("The eye button at the end of a row opens the run's details: who ran it and "
                "when, how long it took, how many rows it gave back or changed, the full "
                "error if it failed, every parameter in a table, and — for an UPDATE or "
                "DELETE — the rows as they were before the change, page by page. If an UPDATE "
                "or DELETE saved no old rows, the window tells you why instead of leaving the "
                "space empty.",
                "زر العين في نهاية الصف يفتح تفاصيل التشغيل: من شغّله ومتى، وكم استغرق، وكم "
                "صفًا أعاد أو غيّر، والخطأ كاملًا إن فشل، وكل معايير البحث في جدول، وفي "
                "UPDATE وDELETE الصفوف كما كانت قبل التغيير مقسّمة إلى صفحات. وإذا لم يحفظ "
                "UPDATE أو DELETE الصفوف القديمة، تخبرك النافذة بالسبب بدل أن تترك المكان "
                "فارغًا."))
    figure(doc, "40a-execution-log-details",
           T("The details of one UPDATE run: its parameters, and the row it changed as it was "
             "before.",
             "تفاصيل تشغيل UPDATE واحد: معايير البحث، والصف الذي غيّره كما كان قبل التغيير."))

    h2(doc, T("14.2 System audit", "١٤.٢ سجل عمليات النظام"))
    para(doc, T("Every change an administrator makes is recorded here: accounts created, "
                "permissions given, queries edited, tasks run, settings changed.",
                "كل تغيير يقوم به المسؤول يُسجَّل هنا: إنشاء الحسابات، وإعطاء الصلاحيات، "
                "وتعديل الاستعلامات، وتشغيل المهام، وتغيير الإعدادات."))
    figure(doc, "41-system-audit",
           T("System Audit. Filter by category and by result; Details opens the saved values "
             "for an entry.",
             "سجل عمليات النظام. صفِّ حسب الفئة والنتيجة، وزر «التفاصيل» يفتح القيم المحفوظة "
             "لكل سطر."))
    bullets(doc, [
        (T("Categories — ", "الفئات — "),
         T("Users, Queries, Query groups, Permissions, Database connections, Scheduled tasks, "
           "Active Directory, Branding, Settings, Other.",
           "المستخدمون، والاستعلامات، ومجموعات الاستعلامات، والصلاحيات، واتصالات قواعد "
           "البيانات، والمهام المجدولة، وActive Directory، والهوية البصرية، والإعدادات، "
           "وأخرى.")),
        (T("Outcome — ", "النتيجة — "),
         T("Succeeded or Refused. Refused attempts are recorded on purpose — “an Access "
           "Manager tried to reset an administrator's password and was refused” is often the "
           "line that matters most.",
           "نجحت أو رُفضت. والمحاولات المرفوضة تُسجَّل عمدًا — فسطر مثل «حاول مدير صلاحيات "
           "تغيير كلمة مرور مسؤول فرُفض» يكون غالبًا هو الأهم.")),
        (T("Details — ", "التفاصيل — "),
         T("the values sent with the change.",
           "القيم التي أُرسلت مع التغيير.")),
    ])
    note(doc, T("This log can only be read in the system: there is no way to edit or delete a "
                "single line. Passwords, keys, connection details and similar values are "
                "replaced with *** before a line is saved, and the contents of uploaded files "
                "are left out.",
                "هذا السجل للقراءة فقط داخل النظام: فلا يمكن تعديل سطر أو حذفه. وكلمات المرور "
                "والمفاتيح وبيانات الاتصال وما يشبهها تُستبدل بـ *** قبل حفظ السطر، ولا يُحفظ "
                "محتوى الملفات المرفوعة."))
    note(doc, T("Both logs are kept forever unless an administrator sets how long to keep "
                "them in System Settings (16.1). Then older lines are deleted every six "
                "hours, and each time something is deleted a line is added here — “Rotated "
                "the logs” — saying how many were deleted from each.",
                "يُحتفظ بالسجلين دائمًا ما لم يحدد المسؤول مدة الاحتفاظ بهما في إعدادات "
                "النظام (١٦.١). وعندها تُحذف الأسطر الأقدم كل ست ساعات، وفي كل مرة يُحذف فيها "
                "شيء يُضاف هنا سطر — «دوّر السجلات» — يذكر عدد ما حُذف من كل سجل."),
        kind="warning")
    figure(doc, "92-auditor-system-audit",
           T("The same page as an Auditor sees it. The content is the same; the menus around "
             "it are not.",
             "الصفحة نفسها كما يراها المدقق. المحتوى نفسه، أما القوائم حوله فمختلفة."))

    page_break(doc)

    # ===================================================================== 15
    h1(doc, T("15. Website branding", "١٥. هوية الموقع"))
    para(doc, T("For: Administrators only.", "لمن: المسؤولون فقط."), italic=True)
    para(doc, T("You can change what the top bar shows: an uploaded logo, or the site's name "
                "in each language. The same window also sets the icon the browser shows on "
                "the tab. Open the account menu and choose Website branding.",
                "يمكنك تغيير ما يظهر في الشريط العلوي: شعار ترفعه، أو اسم الموقع بكل لغة. ومن "
                "النافذة نفسها تغيّر أيضًا الأيقونة التي يعرضها المتصفح في التبويب. افتح "
                "قائمة الحساب واختر «هوية الموقع»."))
    para(doc, T("The bar shows the first of these that exists: the logo if one is uploaded, "
                "otherwise the site name in the current language, otherwise the system's own "
                "name. So the bar is never empty, and removing the logo shows the name "
                "instead.",
                "يعرض الشريط أول ما يجده من هذه بالترتيب: الشعار إن كان مرفوعًا، وإلا اسم "
                "الموقع باللغة الحالية، وإلا اسم النظام نفسه. لذلك لا يبقى الشريط فارغًا "
                "أبدًا، وحذف الشعار يُظهر الاسم مكانه."))
    figure(doc, "58-branding-dialog",
           T("The branding window: the logo at the top, the browser tab icon under it. Scroll "
             "down to see the site name.",
             "نافذة هوية الموقع: الشعار في الأعلى، وأيقونة تبويب المتصفح تحته. انزل للأسفل "
             "لترى اسم الموقع."))
    h2(doc, T("The logo", "الشعار"))
    bullets(doc, [
        (T("File types — ",
           "أنواع الملفات — "),
         T("PNG, JPG or WebP, up to 1 MB. SVG is not accepted.",
           "PNG أو JPG أو WebP بحد أقصى ١ ميغابايت. ولا تُقبل صيغة SVG.")),
        (T("Size — ", "الحجم — "),
         T("the bar shows the logo 40 pixels tall; an image 80 pixels tall is best, so it "
           "stays sharp on high-quality screens. It can be up to 200 pixels wide.",
           "يعرض الشريط الشعار بارتفاع ٤٠ بكسل، والأفضل أن تكون الصورة بارتفاع ٨٠ بكسل لتبقى "
           "واضحة على الشاشات عالية الجودة. وأقصى عرض ٢٠٠ بكسل.")),
        (T("Warnings — ",
           "تحذيرات — "),
         T("the window warns you before uploading if the image is too short, not sharp "
           "enough, or so wide that it will be cut off.",
           "تحذّرك النافذة قبل الرفع إذا كانت الصورة قصيرة جدًا، أو غير واضحة بما يكفي، أو "
           "عريضة جدًا بحيث يُقص جزء منها.")),
    ])
    h2(doc, T("The browser tab icon", "أيقونة تبويب المتصفح"))
    para(doc, T("This is the small picture a browser shows on the tab, in the bookmarks bar "
                "and in history. It is uploaded separately from the logo, not made smaller "
                "from it: a logo is wide and readable at 40 pixels tall, but a tab icon is a "
                "tiny 16-pixel square, and shrinking one into the other gives a blur.",
                "هي الصورة الصغيرة التي يعرضها المتصفح في التبويب وفي شريط المفضلة وفي سجل "
                "التصفح. وتُرفع منفصلة عن الشعار، ولا تُصغَّر منه: فالشعار عريض وواضح بارتفاع "
                "٤٠ بكسل، أما أيقونة التبويب فمربع صغير ١٦ بكسل، وتصغير أحدهما إلى الآخر يعطي "
                "صورة غير واضحة."))
    bullets(doc, [
        (T("File types — ",
           "أنواع الملفات — "),
         T("PNG, ICO or WebP, up to 256 KB. JPG is not accepted, because a tab icon needs a "
           "see-through background, which JPG cannot have.",
           "PNG أو ICO أو WebP بحد أقصى ٢٥٦ كيلوبايت. ولا يُقبل JPG، لأن أيقونة التبويب تحتاج "
           "خلفية شفافة، وملفات JPG لا تكون شفافة.")),
        (T("Size — ", "المقاس — "),
         T("a square image of 32×32 pixels suits the tab and the bookmarks bar. The window "
           "shows it twice: bigger, and at the real size the browser will use.",
           "الصورة المربعة بمقاس ٣٢×٣٢ بكسل تناسب التبويب وشريط المفضلة. وتعرضها النافذة "
           "مرتين: مكبّرة، وبالحجم الحقيقي الذي سيستخدمه المتصفح.")),
        (T("It appears before signing in — ", "تظهر قبل تسجيل الدخول — "),
         T("the sign-in page's tab shows it too, so people see it before they have even "
           "signed in.",
           "تظهر أيضًا في تبويب صفحة الدخول، فيراها الناس قبل أن يدخلوا.")),
        (T("Remove — ", "إزالة — "),
         T("with no icon saved, the browser shows its own standard icon.",
           "إذا لم تكن هناك أيقونة محفوظة يعرض المتصفح أيقونته العادية.")),
    ])
    note(doc, T("Browsers hold on to tab icons for a long time. After changing it, a tab that "
                "was already open may keep the old icon until it is reloaded — open the site "
                "in a new tab to check the change.",
                "المتصفحات تحتفظ بأيقونات التبويب مدة طويلة. فبعد تغيير الأيقونة قد يبقى "
                "التبويب المفتوح من قبل بالأيقونة القديمة حتى تحدّثه — افتح الموقع في تبويب "
                "جديد لتتأكد من التغيير."))

    h2(doc, T("The site name", "اسم الموقع"))
    figure(doc, "58b-branding-dialog-name",
           T("The lower half of the same window: one name box for each language, each showing "
             "how many characters are left.",
             "النصف السفلي من النافذة نفسها: خانة اسم لكل لغة، وتعرض كل منها عدد الأحرف "
             "المتبقية."))
    bullets(doc, [
        (T("One name per language — ", "اسم لكل لغة — "),
         T("the English name is shown to people using the English screens, and the Arabic "
           "name to people using the Arabic ones. They are separate, because a site is rarely "
           "called the same thing in both languages.",
           "يظهر الاسم الإنجليزي لمن يستخدم الشاشات الإنجليزية، والعربي لمن يستخدم الشاشات "
           "العربية. وهما منفصلان، لأن الموقع نادرًا ما يكون له الاسم نفسه في اللغتين.")),
        (T("Either one can be left empty — ",
           "يمكن ترك أي منهما فارغًا — "),
         T("an empty box shows the system's own name for that language only, so filling in "
           "only the Arabic name is fine.",
           "الخانة الفارغة تعرض اسم النظام نفسه لتلك اللغة فقط، لذلك لا بأس بملء الاسم العربي "
           "وحده.")),
        (T("Up to 60 characters — ", "حتى ٦٠ حرفًا — "),
         T("a longer name is refused instead of being cut off on screen, because the bar "
           "shares its width with the menus.",
           "الاسم الأطول يُرفض بدل أن يُقص على الشاشة، لأن الشريط يتقاسم عرضه مع القوائم.")),
        (T("Save — ", "حفظ — "),
         T("both names are saved together, and the bar changes without reloading the page. "
           "Uploading a logo works straight away and needs no Save.",
           "يُحفظ الاسمان معًا، ويتغير الشريط دون إعادة تحميل الصفحة. أما رفع الشعار فيعمل "
           "فورًا ولا يحتاج إلى «حفظ».")),
    ])
    note(doc, T("Remove works straight away, with no confirmation. Keep a copy of the logo "
                "file before you open this window.",
                "زر «إزالة» يعمل فورًا دون طلب تأكيد. احتفظ بنسخة من ملف الشعار قبل فتح هذه "
                "النافذة."),
        kind="warning")
    note(doc, T("Every change here is recorded in the system audit — the logo as “Changed the "
                "site logo”, the tab icon as “Changed the browser tab icon”, and the name as "
                "“Changed the site name”.",
                "كل تغيير في هذه النافذة يُسجَّل في سجل عمليات النظام — الشعار باسم «تغيير "
                "شعار الموقع»، وأيقونة التبويب باسم «تغيير أيقونة تبويب المتصفح»، والاسم باسم "
                "«تغيير اسم الموقع»."))

    # ===================================================================== 16
    h1(doc, T("16. System settings", "١٦. إعدادات النظام"))
    para(doc, T("For: people with “Change system settings” or “Manage roles and permissions”.",
                "لمن: من يملك «تغيير إعدادات النظام» أو «إدارة الأدوار والصلاحيات»."),
         italic=True)
    para(doc, T("Settings holds the choices for the whole system — what each role is allowed "
                "to do, how long a sign-in lasts, and which features are turned on. Changes "
                "work straight away, without restarting, and each one is recorded in the "
                "system audit. Switches are saved as soon as you flip them; the numbers are "
                "changed together and saved with the button under them.",
                "صفحة «الإعدادات» فيها الخيارات التي تخص النظام كله — ما يُسمح لكل دور بفعله، "
                "وكم يبقى الدخول صالحًا، وأي الميزات مفعّلة. والتغييرات تعمل فورًا دون إعادة "
                "تشغيل، ويُسجَّل كل تغيير في سجل عمليات النظام. والمفاتيح تُحفظ بمجرد "
                "تغييرها، أما الأرقام فتُعدَّل معًا وتُحفظ بالزر تحتها."))
    h2(doc, T("16.1 System", "١٦.١ النظام"))
    figure(doc, "50-system-settings", T("System Settings.", "إعدادات النظام."))
    table(doc,
          [T("Setting", "الإعداد"), T("Default", "الافتراضي"), T("What it "
                                                                                 "does",
                                                                                 "ماذا يفعل")],
          [
              [T("Active Directory features are available",
                 "خصائص Active Directory متاحة"),
               T("On", "مفعَّل"),
               T("Turn it off if your company does not use Active Directory: the AD Users "
                 "page is then hidden instead of showing a connection error. Signing in is "
                 "not affected.",
                 "أغلقه إذا كانت شركتك لا تستخدم Active Directory: فتُخفى صفحة «مستخدمو "
                 "Active Directory» بدل أن تعرض خطأ اتصال. ولا يتأثر تسجيل الدخول.")],
              [T("Access token (minutes)", "رمز الوصول (بالدقائق)"),
               "60",
               T("How long a sign-in lasts before it is renewed in the background, between 5 "
                 "and 1440. Shorter means that when an account is turned off, or someone is "
                 "removed from a group, it takes effect sooner.",
                 "المدة التي يبقى فيها الدخول قبل أن يُجدَّد تلقائيًا، بين ٥ و١٤٤٠. وكلما "
                 "كانت أقصر، كان أثر إيقاف حساب أو إخراج شخص من مجموعة أسرع.")],
              [T("Refresh token (days)", "رمز التجديد (بالأيام)"),
               "7",
               T("How long someone can be away and still come back without signing in again, "
                 "between 1 and 90.",
                 "المدة التي يمكن أن يغيب فيها الشخص ثم يعود دون أن يدخل من جديد، بين ١ و٩٠.")],
              [T("Row limit for previews and lookups",
                 "حد الصفوف للمعاينات وقوائم الاختيار"),
               "10000",
               T("Limits how many rows are read at once in places where the whole result is "
                 "kept in memory: the preview of a query that changes data, the old rows it "
                 "saves, and the choices in a dropdown list. The results table and exports "
                 "are not affected.",
                 "يحدد عدد الصفوف التي تُقرأ مرة واحدة في الأماكن التي تُحفظ فيها النتيجة "
                 "كاملة في الذاكرة: معاينة الاستعلام الذي يغيّر البيانات، والصفوف القديمة "
                 "التي يحفظها، وخيارات القائمة المنسدلة. ولا يتأثر جدول النتائج ولا التصدير.")],
              [T("Maximum SQL length (characters)",
                 "الحد الأقصى لطول نص SQL (بالأحرف)"),
               "4000",
               T("The longest SQL a saved query can have, between 1000 and 1000000 "
                 "characters, counting spaces, new lines and comments. Lowering it does not "
                 "affect queries already saved: a longer one keeps working and only has to be "
                 "made shorter the next time someone edits it.",
                 "أطول نص SQL يمكن أن يكون في الاستعلام المحفوظ، بين ١٠٠٠ و١٠٠٠٠٠٠ حرف، مع "
                 "حساب المسافات والأسطر الجديدة والتعليقات. وتقليله لا يؤثر على الاستعلامات "
                 "المحفوظة: فالأطول منها يستمر في العمل، ويجب تقصيره فقط عندما يعدّله أحد في "
                 "المرة القادمة.")],
          ],
          widths=[2.2, 0.8, 3.6])
    note(doc, T("The numbers are checked when you save; a value outside the allowed range is "
                "refused, and the message tells you the range. Making the access token "
                "shorter does not sign out people who are already in — it applies from their "
                "next sign-in or renewal.",
                "تُفحص الأرقام عند الحفظ، والقيمة الخارجة عن المسموح تُرفض وتذكر الرسالة "
                "المدى المسموح. وتقصير مدة رمز الوصول لا يُخرج من هم داخل النظام الآن — بل "
                "يعمل من دخولهم أو تجديدهم التالي."),
        kind="warning")

    h3(doc, T("Log retention", "الاحتفاظ بالسجلات"))
    para(doc, T("The execution logs and the system audit get longer with every run and every "
                "change. Log retention says how long each one is kept; older lines are "
                "deleted automatically — two minutes after the server starts, and then every "
                "six hours.",
                "سجلات التنفيذ وسجل عمليات النظام يزدادان طولًا مع كل تشغيل وكل تغيير. "
                "و«الاحتفاظ بالسجلات» يحدد كم يُحتفظ بكل منهما، وتُحذف الأسطر الأقدم تلقائيًا "
                "— بعد دقيقتين من تشغيل الخادم، ثم كل ست ساعات."))
    figure(doc, "50a-log-retention",
           T("Log retention. Under each box, the saved value in words.",
             "الاحتفاظ بالسجلات. وتحت كل خانة، القيمة المحفوظة مكتوبة بالكلمات."))
    table(doc,
          [T("Setting", "الإعداد"), T("Default", "الافتراضي"), T("What it "
                                                                                 "does",
                                                                                 "ماذا يفعل")],
          [
              [T("Keep execution logs for (days)", "الاحتفاظ بسجلات التنفيذ لمدة (بالأيام)"),
               "0",
               T("Every run of a query, with its parameters and old rows. 0 keeps them "
                 "forever; otherwise 7 to 3650 days.",
                 "كل تشغيل لاستعلام، مع معايير البحث والصفوف القديمة. القيمة ٠ تعني الاحتفاظ "
                 "بها دائمًا، وإلا فمن ٧ إلى ٣٦٥٠ يومًا.")],
              [T("Keep system audit for (days)", "الاحتفاظ بتدقيق النظام لمدة (بالأيام)"),
               "0",
               T("The changes administrators made (14.2), with the same rules. It is set "
                 "separately, because many companies keep a record of changes much longer "
                 "than a record of query runs.",
                 "التغييرات التي قام بها المسؤولون (١٤.٢)، بالقواعد نفسها. وتُضبط وحدها، لأن "
                 "شركات كثيرة تحتفظ بسجل التغييرات مدة أطول بكثير من سجل تشغيل الاستعلامات.")],
          ],
          widths=[2.2, 0.8, 3.6])
    bullets(doc, [
        (T("Saving does not delete anything by itself. ",
           "الحفظ وحده لا يحذف شيئًا. "),
         T("The next automatic clean-up does — or Rotate now, which asks you first, then "
           "deletes what is older than the saved periods and tells you how many lines were "
           "deleted from each log.",
           "بل يحذف التنظيف التلقائي التالي — أو زر «تدوير الآن»، الذي يسألك أولًا، ثم يحذف "
           "ما هو أقدم من المدة المحفوظة ويخبرك كم سطرًا حُذف من كل سجل.")),
        (T("Rotate now cannot be pressed ",
           "لا يمكن الضغط على «تدوير الآن» "),
         T("while both periods are 0, and while there are changes you have not saved — save "
           "or cancel them first, so it never runs with a value you did not save.",
           "ما دامت المدتان ٠، وما دامت هناك تغييرات لم تحفظها — فاحفظها أو ألغها أولًا، حتى "
           "لا يعمل أبدًا بقيمة لم تحفظها.")),
    ])
    note(doc, T("Deleted lines cannot be brought back. If a rule or law says how long logs "
                "must be kept, set the period to at least that.",
                "الأسطر المحذوفة لا يمكن استرجاعها. وإذا كان هناك نظام أو قانون يحدد مدة يجب "
                "الاحتفاظ فيها بالسجلات، فاجعل المدة لا تقل عنها."),
        kind="warning")

    h2(doc, T("16.2 Permissions", "١٦.٢ الصلاحيات"))
    para(doc, T("The second tab shows all permissions in one table: every permission down the "
                "side, every role across the top, and a tick where the role has it. "
                "Everything any role can do is decided here.",
                "التبويب الثاني يعرض كل الصلاحيات في جدول واحد: كل صلاحية في الجانب، وكل دور "
                "في الأعلى، وعلامة حيث يملك الدور الصلاحية. وهنا يُحدَّد كل ما يستطيعه أي "
                "دور."))
    figure(doc, "51-permissions",
           T("Settings → Permissions. Admin's column is ticked and locked; you set the rest.",
             "«الإعدادات ← الصلاحيات». عمود Admin محدد ومقفل، والباقي تحدده أنت."))
    numbered(doc, [
        T("Tick or untick the permissions you want a role to have. Nothing is saved yet — the "
          "number of changed roles appears at the bottom.",
          "حدّد الصلاحيات التي تريدها لدور ما أو أزل تحديدها. ولا يُحفظ شيء بعد — ويظهر عدد "
          "الأدوار التي تغيّرت في الأسفل."),
        T("Press Save. Only the roles you changed are saved, and each change is recorded in "
          "the system audit with what was given and what was taken away.",
          "اضغط «حفظ». لا يُحفظ إلا ما غيّرته من أدوار، ويُسجَّل كل تغيير في سجل عمليات "
          "النظام مع ما أُعطي وما سُحب."),
        T("The change applies to everyone with that role from their next click. Nobody has to "
          "sign out.",
          "ويعمل التغيير لكل من له هذا الدور من نقرته التالية، دون حاجة إلى الخروج."),
    ])
    para(doc, T("Add role creates a role with no permissions at all; tick what it can do and "
                "save. A role you created can be deleted from the top of its column — but not "
                "while any account still has it, because you need to decide where those "
                "people go first.",
                "زر «إضافة دور» ينشئ دورًا بلا أي صلاحية، فحدّد ما يستطيعه ثم احفظ. ويمكن حذف "
                "الدور الذي أنشأته من أعلى عموده — لكن ليس ما دام لأي حساب، لأن عليك أن تقرر "
                "أولًا أين يذهب هؤلاء الأشخاص."))
    note(doc, T("The four roles that come with the system can have their permissions changed, "
                "but cannot be renamed or deleted, and Admin cannot be changed at all. That "
                "last rule makes sure there is always someone who can open this page.",
                "الأدوار الأربعة الجاهزة يمكن تغيير صلاحياتها، لكن لا يمكن تغيير أسمائها ولا "
                "حذفها، أما Admin فلا يمكن تغييره أبدًا. وهذه القاعدة الأخيرة تضمن أن يبقى "
                "دائمًا من يستطيع فتح هذه الصفحة."),
        kind="warning")

    page_break(doc)

    # ===================================================================== 17
    h1(doc, T("17. Appendix A — Frequently asked questions",
              "١٧. الملحق أ — أسئلة شائعة"))
    table(doc,
          [T("Question", "السؤال"), T("Answer", "الجواب")],
          [
              [T("My Queries is empty.", "صفحة «الاستعلامات» فارغة."),
               T("Nothing has been given to you yet. Ask an administrator or Access Manager "
                 "to give you the query or its group, directly or through a user group.",
                 "لم يُعطَ لك شيء بعد. اطلب من المسؤول أو مدير الصلاحيات أن يعطيك الاستعلام "
                 "أو مجموعته، مباشرة أو عن طريق مجموعة مستخدمين.")],
              [T("I can see a query but cannot run it.",
                 "أرى استعلامًا لكني لا أستطيع تشغيله."),
               T("It may be disabled, or you may not be allowed to use the database "
                 "connection it uses. Both are checked when you run it.",
                 "ربما يكون معطّلًا، أو ربما لا يُسمح لك باستخدام اتصال قاعدة البيانات الذي "
                 "يستخدمه. ويُفحص الأمران عند التشغيل.")],
              [T("There is no Dashboards entry in my top bar.",
                 "لا يظهر «لوحات المعلومات» في الشريط العلوي عندي."),
               T("None of your roles has View dashboards. At the start only Admin has it; ask "
                 "an administrator to add it to your role.",
                 "لا يملك أي دور من أدوارك صلاحية «عرض لوحات المعلومات»، وفي البداية لا "
                 "يملكها إلا Admin. اطلب من المسؤول أن يضيفها إلى دورك.")],
              [T("One tile on a dashboard shows a message instead of data.",
                 "يعرض أحد عناصر اللوحة رسالة بدل البيانات."),
               T("Usually you can open the dashboard but not that tile's query — each tile is "
                 "checked by itself. If the message mentions a column, the query has changed "
                 "and an administrator needs to update the tile.",
                 "غالبًا تستطيع فتح اللوحة لكن ليس استعلام ذلك العنصر — فكل عنصر يُفحص وحده. "
                 "وإذا ذكرت الرسالة عمودًا فقد تغيّر الاستعلام ويحتاج المسؤول إلى تحديث "
                 "العنصر.")],
              [T("I pressed Back and my results are gone.",
                 "ضغطت «رجوع» فاختفت نتائجي."),
               T("Leaving the results page throws away the result kept on the server. Run the "
                 "query again.",
                 "مغادرة صفحة النتائج تحذف النتيجة المحفوظة على الخادم. شغّل الاستعلام مرة "
                 "أخرى.")],
              [T("Export only gave me part of the result.",
                 "التصدير أعطاني جزءًا من النتيجة فقط."),
               T("It does not — export always saves the whole result, not just the page on "
                 "screen. The table shows one page at a time on purpose.",
                 "هذا غير صحيح — فالتصدير يحفظ النتيجة كاملة دائمًا وليس الصفحة المعروضة فقط. "
                 "والجدول يعرض صفحة واحدة في كل مرة عمدًا.")],
              [T("I confirmed a change by mistake.",
                 "أكّدت تغييرًا بالخطأ."),
               T("The change has been saved. Use the old values in the execution log to see "
                 "what the rows were before.",
                 "التغيير حُفظ. استخدم القيم القديمة في سجل التنفيذ لترى ما كانت عليه الصفوف "
                 "قبل التغيير.")],
              [T("A scheduled file will not download.",
                 "ملف مهمة مجدولة لا يتنزّل."),
               T("The file was moved, deleted, or replaced by a later run. Run the task "
                 "again, or check the output folder on the server.",
                 "نُقل الملف أو حُذف أو استُبدل بتشغيل لاحق. شغّل المهمة مرة أخرى، أو افحص "
                 "مجلد الإخراج على الخادم.")],
              [T("I am an Auditor and cannot run anything.",
                 "أنا مدقق ولا أستطيع تشغيل أي شيء."),
               T("That is on purpose. The Auditor role can never be given queries. If you "
                 "also need to run queries, ask for the User role as well.",
                 "هذا مقصود. فدور Auditor لا يمكن أن يُعطى استعلامات أبدًا. وإذا كنت تحتاج "
                 "إلى التشغيل أيضًا، فاطلب إضافة دور User.")],
              [T("My imported query is named “… (imported)”.",
                 "الاستعلام الذي استوردته اسمه «… (imported)»."),
               T("A query with that name was already there. Import never replaces anything — "
                 "compare the two and delete the one you do not want.",
                 "كان هناك استعلام بالاسم نفسه. والاستيراد لا يستبدل أي شيء — قارن بين "
                 "الاثنين واحذف الذي لا تريده.")],
              [T("The same query appears in two folders on My Queries.",
                 "يظهر الاستعلام نفسه في مجلدين في «الاستعلامات»."),
               T("It is in both groups. It is still one query — running it from either folder "
                 "is the same.",
                 "لأنه موجود في المجموعتين. وهو استعلام واحد — فتشغيله من أي المجلدين هو "
                 "الشيء نفسه.")],
              [T("Old execution logs or audit lines have gone.",
                 "اختفت سجلات تنفيذ أو أسطر تدقيق قديمة."),
               T("Someone set how long logs are kept in System Settings (16.1), and older "
                 "lines are deleted. The system audit records each clean-up and how many "
                 "lines it deleted.",
                 "حدد أحدهم مدة الاحتفاظ بالسجلات في إعدادات النظام (١٦.١)، فتُحذف الأسطر "
                 "الأقدم منها. ويسجّل سجل عمليات النظام كل عملية تنظيف وعدد الأسطر التي "
                 "حذفتها.")],
          ],
          widths=[2.2, 4.4])

    h1(doc, T("18. Appendix B — Making this manual again",
              "١٨. الملحق ب — إعادة إنشاء هذا الدليل"))
    para(doc, T("This document is made by a program, not typed by hand. After the system "
                "changes, make it again so the pictures and text match the new version.",
                "هذا المستند يصنعه برنامج، ولا يُكتب يدويًا. وبعد أي تغيير في النظام أعد "
                "إنشاءه حتى تطابق الصور والنصوص النسخة الجديدة."))
    numbered(doc, [
        T("Start the API and the Angular client (see the project README).",
          "شغّل خادم API وواجهة Angular (انظر ملف README للمشروع)."),
        T("From docs/user-manual, run:  npm install   (first time only)",
          "من المجلد docs/user-manual نفّذ:  npm install   (في المرة الأولى فقط)"),
        T("Run:  npm run manual", "نفّذ:  npm run manual"),
    ])
    para(doc, T("That takes every picture again in both languages, into "
                "docs/user-manual/screenshots, and writes both documents again. The text is "
                "in build-docx.py; the list of pictures is in capture-screenshots.js. Neither "
                "one changes the system's data — while the pictures are being taken, every "
                "request that would change something is blocked.",
                "يعيد ذلك التقاط كل الصور باللغتين في docs/user-manual/screenshots، ويكتب "
                "المستندين من جديد. والنص موجود في build-docx.py، وقائمة الصور في "
                "capture-screenshots.js. ولا يغيّر أي منهما بيانات النظام — فأثناء التقاط "
                "الصور يُمنع كل طلب قد يغيّر شيئًا."))
    para(doc, T("Open the finished document in Word, right-click the Contents table and "
                "choose Update Field to fill in the page numbers.",
                "افتح المستند في Word، وانقر بزر الفأرة الأيمن على جدول المحتويات واختر "
                "«تحديث الحقل» لتظهر أرقام الصفحات."))


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

    # MANUAL_OUT_DIR exists because the README tells you to open the finished document in Word
    # to refresh its table of contents — and Word holds the file open, so the next build cannot
    # overwrite it. Building elsewhere lets the run finish and the file be moved into place after.
    out_dir = os.environ.get("MANUAL_OUT_DIR") or HERE
    out = os.path.join(out_dir, f"Jadwal-User-Manual-{lang.upper()}.docx")
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
