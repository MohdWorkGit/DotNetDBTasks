using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bayan.Application.Common.Models;

namespace Bayan.Infrastructure.Services;

/// <summary>
/// Generates a Word (.docx) document from query results using only the built-in
/// <see cref="ZipArchive"/> — no third-party package, so it works inside the air-gapped
/// offline bundle (same approach as <see cref="ExcelExporter"/>).
///
/// Every export fills a template: the caller-supplied .docx (per-query or system default)
/// or, when none is given, the built-in starter template from
/// <see cref="BuildStarterTemplate"/> — the same file admins can download, edit in Word,
/// and upload back as the system default. {{QUERY_NAME}}, {{GENERATED_AT}} and
/// {{ROW_COUNT}} are replaced as text anywhere in the body, headers and footers
/// (paragraph granularity: a matching paragraph keeps its first run's formatting).
/// Each query parameter is exposed the same way it is referenced in the SQL: a
/// {{@paramName}} placeholder is replaced with the value the query ran with (multi-value
/// parameters join their values with ", "). {{PARAMS}} expands to every parameter as
/// "Display Name: value", one per line.
///
/// {{RESULTS}} marks where the result table goes, and the template controls the table's
/// styling: when the marker sits INSIDE a table, that table is the styling prototype —
/// its first row styles the header, the marker's row styles the data rows, the row
/// directly below the marker (when present) styles alternating rows, and any other rows
/// (e.g. a totals row) are kept in place. When the marker is a plain paragraph, a
/// neutral built-in table style is used.
/// </summary>
internal static partial class WordExporter
{
    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string XmlDeclaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
    private static readonly XNamespace W = WordNs;

    public static byte[] Export(
        IReadOnlyList<ExportResultSet> results,
        string title,
        bool includeHeaders,
        byte[]? template = null,
        IReadOnlyList<ExportParameter>? parameters = null)
    {
        var rowCount = results.Sum(r => r.Rows.Count);
        var replacements = new Dictionary<string, string>
        {
            ["{{QUERY_NAME}}"] = title,
            ["{{GENERATED_AT}}"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["{{ROW_COUNT}}"] = rowCount.ToString(CultureInfo.InvariantCulture)
        };

        if (parameters is { Count: > 0 })
        {
            // Expose each parameter as {{@name}} — the same @name it is bound by in the SQL.
            foreach (var parameter in parameters)
                replacements["{{@" + parameter.Name + "}}"] = FormatParameterValue(parameter.Value);

            // {{PARAMS}} — every parameter as "Display Name: value", one per line.
            replacements["{{PARAMS}}"] = string.Join("\n",
                parameters.Select(p =>
                    $"{(string.IsNullOrWhiteSpace(p.DisplayName) ? p.Name : p.DisplayName)}: {FormatParameterValue(p.Value)}"));
        }

        return FillTemplate(template ?? BuildStarterTemplate(), results, includeHeaders, replacements);
    }

    /// <summary>
    /// Renders a bound parameter value for a placeholder: multi-value parameters (bound as a
    /// sequence for IN clauses) join with ", "; everything else uses the same culture-invariant
    /// formatting as result cells.
    /// </summary>
    private static string FormatParameterValue(object? value)
    {
        if (value is null or DBNull)
            return string.Empty;
        if (value is not string && value is not byte[] && value is System.Collections.IEnumerable sequence)
            return string.Join(", ", sequence.Cast<object?>().Select(ResultFileExporter.FormatValue));
        return ResultFileExporter.FormatValue(value);
    }

    // ---------- starter template ----------

    /// <summary>
    /// The built-in default layout as an editable .docx template: landscape A4 with a
    /// {{QUERY_NAME}} title, a "Generated {{GENERATED_AT}}" subtitle and a prototype
    /// table holding the {{RESULTS}} marker — restyling that table in Word restyles the
    /// exported result table. Exposed so admins can download it as a starting point for
    /// their own default template, and used directly when no template is stored.
    /// </summary>
    public static byte[] BuildStarterTemplate()
    {
        var body = new StringBuilder();
        body.Append("<w:p><w:pPr><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr></w:pPr>")
            .Append("<w:r><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr>")
            .Append("<w:t xml:space=\"preserve\">{{QUERY_NAME}}</w:t></w:r></w:p>");
        body.Append("<w:p><w:pPr><w:rPr><w:color w:val=\"666666\"/><w:sz w:val=\"16\"/></w:rPr></w:pPr>")
            .Append("<w:r><w:rPr><w:color w:val=\"666666\"/><w:sz w:val=\"16\"/></w:rPr>")
            .Append("<w:t xml:space=\"preserve\">Generated {{GENERATED_AT}}  |  {{ROW_COUNT}} row(s)</w:t></w:r></w:p>");

        // Prototype result table: row 1 styles the header, row 2 (the marker row) styles
        // the data rows. Admins restyle these two rows in Word to restyle every export.
        body.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/>")
            .Append("<w:tblBorders>")
            .Append("<w:top w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
            .Append("<w:left w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
            .Append("<w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
            .Append("<w:right w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
            .Append("<w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
            .Append("<w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
            .Append("</w:tblBorders><w:tblLayout w:type=\"autofit\"/></w:tblPr>")
            .Append("<w:tblGrid><w:gridCol/></w:tblGrid>")
            .Append("<w:tr><w:trPr><w:tblHeader/></w:trPr>")
            .Append("<w:tc><w:tcPr><w:shd w:val=\"clear\" w:fill=\"E7E7E7\"/></w:tcPr>")
            .Append("<w:p><w:pPr><w:rPr><w:b/><w:sz w:val=\"18\"/></w:rPr></w:pPr>")
            .Append("<w:r><w:rPr><w:b/><w:sz w:val=\"18\"/></w:rPr><w:t>Column Header</w:t></w:r></w:p></w:tc></w:tr>")
            .Append("<w:tr><w:tc><w:tcPr/>")
            .Append("<w:p><w:pPr><w:rPr><w:sz w:val=\"18\"/></w:rPr></w:pPr>")
            .Append("<w:r><w:rPr><w:sz w:val=\"18\"/></w:rPr><w:t>{{RESULTS}}</w:t></w:r></w:p></w:tc></w:tr>")
            .Append("</w:tbl><w:p/>");

        var documentXml =
            XmlDeclaration +
            $"<w:document xmlns:w=\"{WordNs}\"><w:body>" + body +
            // Landscape A4 with 0.5" margins so wide result tables get maximum room.
            "<w:sectPr><w:pgSz w:w=\"16838\" w:h=\"11906\" w:orient=\"landscape\"/>" +
            "<w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" w:header=\"720\" w:footer=\"720\"/></w:sectPr>" +
            "</w:body></w:document>";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>");
            WriteEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>");
            WriteEntry(zip, "word/document.xml", documentXml);
        }
        return ms.ToArray();
    }

    // ---------- template filling ----------

    private static byte[] FillTemplate(
        byte[] template,
        IReadOnlyList<ExportResultSet> results,
        bool includeHeaders,
        IReadOnlyDictionary<string, string> replacements)
    {
        using var ms = new MemoryStream();
        ms.Write(template);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            foreach (var entry in zip.Entries.ToList())
            {
                var isMainDocument = entry.FullName == "word/document.xml";
                var isDocumentPart = isMainDocument ||
                    (entry.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".xml")) ||
                    (entry.FullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".xml"));
                if (!isDocumentPart)
                    continue;

                string xml;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8, leaveOpen: false))
                    xml = reader.ReadToEnd();

                // Tables are only injected into the main body; in headers/footers the
                // {{RESULTS}} marker is simply removed.
                var processed = InjectResults(xml, results, includeHeaders, allowTable: isMainDocument);
                processed = ReplaceInParagraphs(processed, replacements);
                if (processed == xml)
                    continue;

                var name = entry.FullName;
                entry.Delete();
                var newEntry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var writer = new StreamWriter(newEntry.Open(), new UTF8Encoding(false));
                writer.Write(processed);
            }
        }
        return ms.ToArray();
    }

    // ---------- {{RESULTS}} injection ----------

    /// <summary>
    /// Replaces every {{RESULTS}} marker with the result rows. A marker inside a table
    /// turns that table into the styling prototype (see class docs); a marker in a plain
    /// paragraph gets the neutral built-in table.
    /// </summary>
    private static string InjectResults(
        string xml,
        IReadOnlyList<ExportResultSet> results,
        bool includeHeaders,
        bool allowTable)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var markers = doc.Descendants(W + "p")
            .Where(p => ParagraphText(p).Contains("{{RESULTS}}"))
            .ToList();
        if (markers.Count == 0)
            return xml;

        foreach (var marker in markers)
        {
            if (marker.Document != doc)
                continue; // already detached by a previous replacement in the same table

            if (!allowTable)
            {
                // Cells must keep at least one paragraph, so blank the marker instead of removing it.
                marker.ReplaceWith(new XElement(W + "p"));
                continue;
            }

            var prototype = marker.Ancestors(W + "tbl").FirstOrDefault();
            if (prototype is not null)
            {
                prototype.ReplaceWith(BuildTableFromPrototype(prototype, marker, results, includeHeaders));
            }
            else
            {
                var fragment = XElement.Parse(
                    $"<f xmlns:w=\"{WordNs}\">" + BuildTableXml(results, includeHeaders) + "<w:p/></f>");
                marker.ReplaceWith(fragment.Elements().Cast<object>().ToArray());
            }
        }

        return XmlDeclaration + doc.Root!.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Builds the result table by cloning a prototype table's formatting: first row →
    /// header style, marker row → data-row style, row below the marker (if any) →
    /// alternating-row style. All other rows are kept where they are, so templates can
    /// have e.g. a trailing totals row using {{ROW_COUNT}}.
    /// </summary>
    private static XElement BuildTableFromPrototype(
        XElement prototype,
        XElement marker,
        IReadOnlyList<ExportResultSet> results,
        bool includeHeaders)
    {
        var columns = results.Count > 0 ? results[0].Columns : Array.Empty<string>();
        var rows = prototype.Elements(W + "tr").ToList();
        if (rows.Count == 0)
            return new XElement(prototype);
        var markerRow = marker.Ancestors(W + "tr").FirstOrDefault(r => r.Parent == prototype) ?? rows[0];
        int markerIndex = rows.IndexOf(markerRow);

        var headerProto = markerIndex > 0 ? rows[0] : markerRow;
        var altProto = markerIndex + 1 < rows.Count ? rows[markerIndex + 1] : null;

        var table = new XElement(W + "tbl");
        foreach (var element in prototype.Elements())
        {
            if (element.Name == W + "tr")
                break; // rows are handled below
            if (element.Name == W + "tblGrid")
            {
                table.Add(element.Elements(W + "gridCol").Count() == columns.Count
                    ? new XElement(element)
                    : NewGrid(columns.Count));
            }
            else
            {
                table.Add(new XElement(element));
            }
        }
        if (table.Element(W + "tblGrid") is null)
            table.Add(NewGrid(columns.Count));

        // When the generated column count differs from the prototype's, per-cell widths
        // from the prototype would missize every column — Word re-balances without them.
        var stripCellWidths = markerRow.Elements(W + "tc").Count() != columns.Count;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (i == 0 && row == headerProto && row != markerRow)
            {
                if (includeHeaders && columns.Count > 0)
                    table.Add(CloneRow(headerProto, columns, stripCellWidths));
                continue;
            }
            if (row == markerRow)
            {
                if (includeHeaders && headerProto == markerRow && columns.Count > 0)
                    table.Add(CloneRow(markerRow, columns, stripCellWidths));
                int n = 0;
                foreach (var values in EnumerateRowValues(results, columns))
                {
                    var proto = altProto is not null && n % 2 == 1 ? altProto : markerRow;
                    table.Add(CloneRow(proto, values, stripCellWidths));
                    n++;
                }
                continue;
            }
            if (row == altProto)
                continue; // consumed as the alternating-row prototype
            table.Add(StretchRow(new XElement(row), columns.Count));
        }
        return table;
    }

    /// <summary>
    /// Makes a kept prototype row (e.g. a totals row) span the whole generated table by
    /// extending its last cell across the remaining grid columns. Rows that already use
    /// merged cells are left untouched.
    /// </summary>
    private static XElement StretchRow(XElement row, int columnCount)
    {
        var cells = row.Elements(W + "tc").ToList();
        if (cells.Count == 0 || cells.Count >= columnCount ||
            cells.Any(c => c.Element(W + "tcPr")?.Element(W + "gridSpan") is not null))
            return row;

        var last = cells[^1];
        var tcPr = last.Element(W + "tcPr");
        if (tcPr is null)
        {
            tcPr = new XElement(W + "tcPr");
            last.AddFirst(tcPr);
        }
        tcPr.Add(new XElement(W + "gridSpan",
            new XAttribute(W + "val", columnCount - cells.Count + 1)));
        return row;
    }

    private static IEnumerable<IReadOnlyList<string>> EnumerateRowValues(
        IReadOnlyList<ExportResultSet> results,
        IReadOnlyList<string> columns)
    {
        foreach (var result in results)
        {
            foreach (var row in result.Rows)
            {
                var line = new string[columns.Count];
                for (int c = 0; c < columns.Count; c++)
                {
                    object? value = null;
                    if (c < result.Columns.Count)
                        row.TryGetValue(result.Columns[c], out value);
                    line[c] = ResultFileExporter.FormatValue(value);
                }
                yield return line;
            }
        }
    }

    private static XElement NewGrid(int columnCount)
    {
        var grid = new XElement(W + "tblGrid");
        for (int c = 0; c < columnCount; c++)
            grid.Add(new XElement(W + "gridCol"));
        return grid;
    }

    /// <summary>
    /// Clones a prototype row with new cell texts. Each generated cell copies the
    /// corresponding prototype cell's properties and text formatting (extra columns
    /// reuse the last prototype cell).
    /// </summary>
    private static XElement CloneRow(XElement protoRow, IReadOnlyList<string> texts, bool stripCellWidths)
    {
        var protoCells = protoRow.Elements(W + "tc").ToList();
        if (protoCells.Count == 0)
            return new XElement(protoRow);

        var tr = new XElement(W + "tr");
        if (protoRow.Element(W + "trPr") is { } trPr)
            tr.Add(new XElement(trPr));

        for (int i = 0; i < texts.Count; i++)
        {
            var proto = protoCells[Math.Min(i, protoCells.Count - 1)];
            var tc = new XElement(W + "tc");
            if (proto.Element(W + "tcPr") is { } tcPr)
            {
                var cloned = new XElement(tcPr);
                if (stripCellWidths)
                    cloned.Element(W + "tcW")?.Remove();
                tc.Add(cloned);
            }

            var protoP = proto.Elements(W + "p").FirstOrDefault();
            var pPr = protoP?.Element(W + "pPr");
            var rPr = protoP?.Elements(W + "r").Select(r => r.Element(W + "rPr")).FirstOrDefault(x => x is not null)
                      ?? pPr?.Element(W + "rPr");

            var p = new XElement(W + "p");
            if (pPr is not null) p.Add(new XElement(pPr));
            var run = new XElement(W + "r");
            if (rPr is not null) run.Add(new XElement(rPr));
            run.Add(new XElement(W + "t",
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                StripInvalidXmlChars(texts[i])));
            p.Add(run);
            tc.Add(p);
            tr.Add(tc);
        }
        return tr;
    }

    private static string ParagraphText(XElement paragraph) =>
        string.Concat(paragraph.Descendants(W + "t").Select(t => t.Value));

    // ---------- text placeholders ----------

    /// <summary>
    /// Replaces text placeholders paragraph by paragraph. Word routinely splits typed text
    /// across several runs (spell-check bookkeeping), so placeholders are detected on the
    /// paragraph's concatenated text, and a matching paragraph is rebuilt as a single run
    /// that keeps the paragraph's properties and the first run's character formatting.
    /// </summary>
    private static string ReplaceInParagraphs(
        string xml,
        IReadOnlyDictionary<string, string> replacements)
    {
        return ParagraphRegex().Replace(xml, match =>
        {
            var paragraph = match.Value;
            if (!paragraph.Contains("<w:t"))
                return paragraph;

            var text = string.Concat(
                TextRunRegex().Matches(paragraph).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)));
            if (!text.Contains("{{"))
                return paragraph;

            var replaced = text;
            foreach (var (token, value) in replacements)
                replaced = replaced.Replace(token, value);
            if (replaced == text)
                return paragraph;

            var pPr = ParagraphPropsRegex().Match(paragraph).Value;
            var rPr = RunPropsRegex().Match(paragraph).Value;
            var openTag = paragraph[..(paragraph.IndexOf('>') + 1)];
            // Embedded newlines (e.g. the {{PARAMS}} list) become real Word line breaks so a
            // multi-line replacement doesn't collapse onto one line.
            var body = EscapeXml(replaced).Replace("\r\n", "\n").Replace("\r", "\n")
                .Replace("\n", "</w:t><w:br/><w:t xml:space=\"preserve\">");
            return openTag + pPr +
                "<w:r>" + rPr + "<w:t xml:space=\"preserve\">" + body + "</w:t></w:r></w:p>";
        });
    }

    [GeneratedRegex("""<w:p(?: [^>]*)?>.*?</w:p>""", RegexOptions.Singleline)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex("""<w:t(?: [^>]*)?>(.*?)</w:t>""", RegexOptions.Singleline)]
    private static partial Regex TextRunRegex();

    [GeneratedRegex("""<w:pPr(?: [^>]*)?>.*?</w:pPr>""", RegexOptions.Singleline)]
    private static partial Regex ParagraphPropsRegex();

    [GeneratedRegex("""<w:rPr(?: [^>]*)?>.*?</w:rPr>""", RegexOptions.Singleline)]
    private static partial Regex RunPropsRegex();

    // ---------- built-in result table (marker outside any table) ----------

    private static string BuildTableXml(IReadOnlyList<ExportResultSet> results, bool includeHeaders)
    {
        var columns = results.Count > 0 ? results[0].Columns : Array.Empty<string>();
        var sb = new StringBuilder();
        sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/>")
          .Append("<w:tblBorders>")
          .Append("<w:top w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
          .Append("<w:left w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
          .Append("<w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
          .Append("<w:right w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
          .Append("<w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
          .Append("<w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"BBBBBB\"/>")
          .Append("</w:tblBorders><w:tblLayout w:type=\"autofit\"/></w:tblPr>");

        sb.Append("<w:tblGrid>");
        for (int c = 0; c < columns.Count; c++)
            sb.Append("<w:gridCol/>");
        sb.Append("</w:tblGrid>");

        if (includeHeaders && columns.Count > 0)
        {
            // <w:tblHeader/> repeats this row at the top of every page.
            sb.Append("<w:tr><w:trPr><w:tblHeader/></w:trPr>");
            foreach (var column in columns)
                AppendCell(sb, column, bold: true, shading: "E7E7E7");
            sb.Append("</w:tr>");
        }

        foreach (var values in EnumerateRowValues(results, columns))
        {
            sb.Append("<w:tr>");
            foreach (var value in values)
                AppendCell(sb, value, bold: false, shading: null);
            sb.Append("</w:tr>");
        }

        sb.Append("</w:tbl>");
        return sb.ToString();
    }

    private static void AppendCell(StringBuilder sb, string text, bool bold, string? shading)
    {
        sb.Append("<w:tc><w:tcPr>");
        if (shading is not null)
            sb.Append("<w:shd w:val=\"clear\" w:fill=\"").Append(shading).Append("\"/>");
        sb.Append("</w:tcPr><w:p><w:pPr><w:rPr>");
        if (bold) sb.Append("<w:b/>");
        sb.Append("<w:sz w:val=\"18\"/></w:rPr></w:pPr><w:r><w:rPr>");
        if (bold) sb.Append("<w:b/>");
        sb.Append("<w:sz w:val=\"18\"/></w:rPr><w:t xml:space=\"preserve\">")
          .Append(EscapeXml(text)).Append("</w:t></w:r></w:p></w:tc>");
    }

    // ---------- helpers ----------

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string EscapeXml(string value) =>
        SecurityElement.Escape(StripInvalidXmlChars(value)) ?? string.Empty;

    /// <summary>Removes chars illegal in XML 1.0 (raw DB control chars) that would corrupt the part.</summary>
    private static string StripInvalidXmlChars(string value)
    {
        if (string.IsNullOrEmpty(value) || value.All(IsLegalXmlChar))
            return value;
        return new string(value.Where(IsLegalXmlChar).ToArray());
    }

    private static bool IsLegalXmlChar(char ch) =>
        ch == '\t' || ch == '\n' || ch == '\r' ||
        (ch >= ' ' && ch <= '\uD7FF') ||
        (ch >= '\uE000' && ch <= '\uFFFD');
}
