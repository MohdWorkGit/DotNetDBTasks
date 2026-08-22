using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bayan.Application.Common.Models;
using Bayan.Domain.Exceptions;

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
///
/// <para>
/// A <b>report</b> passes several result sets that each carry an <see cref="ExportResultSet.Key"/>,
/// and addresses them individually: {{RESULTS:sales}} renders just that dataset, with the same
/// prototype-table styling rules. {{ROW_COUNT:sales}} is that dataset's row count and
/// {{VALUE:sales.Total}} is its first row's cell, for KPI lines. Bare {{RESULTS}} is unchanged
/// and still appends every set into one table, which is what combined scheduled tasks rely on,
/// so an existing per-query template renders exactly as it did before.
/// </para>
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
        IReadOnlyList<ExportParameter>? parameters = null,
        IReadOnlyList<ReportChartData>? charts = null)
    {
        var rowCount = results.Sum(r => r.Rows.Count);
        var replacements = new Dictionary<string, string>
        {
            ["{{QUERY_NAME}}"] = title,
            // The same value under the name a report author would reach for.
            ["{{REPORT_NAME}}"] = title,
            ["{{GENERATED_AT}}"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["{{ROW_COUNT}}"] = rowCount.ToString(CultureInfo.InvariantCulture)
        };

        // Per-dataset scalars, for reports. Only keyed sets get these: an unkeyed set is part
        // of the append-into-one-table behaviour and has nothing to be addressed by.
        foreach (var set in results)
        {
            if (string.IsNullOrEmpty(set.Key))
                continue;

            replacements["{{ROW_COUNT:" + set.Key + "}}"] =
                set.Rows.Count.ToString(CultureInfo.InvariantCulture);

            // {{VALUE:key.Column}} — the first row's cell, for a KPI line above the table.
            var firstRow = set.Rows.Count > 0 ? set.Rows[0] : null;
            foreach (var column in set.Columns)
            {
                object? value = null;
                firstRow?.TryGetValue(column, out value);
                replacements["{{VALUE:" + set.Key + "." + column + "}}"] =
                    ResultFileExporter.FormatValue(value);
            }
        }

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

        return FillTemplate(
            template ?? BuildStarterTemplate(), results, includeHeaders, replacements,
            charts ?? Array.Empty<ReportChartData>());
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
        AppendHeading(body, "{{QUERY_NAME}}");
        AppendSubtitle(body, "Generated {{GENERATED_AT}}  |  {{ROW_COUNT}} row(s)");
        AppendPrototypeTable(body, "{{RESULTS}}");
        return BuildDocx(body.ToString());
    }

    /// <summary>
    /// The starter template for a <b>report</b>, pre-populated with that report's actual
    /// dataset keys — one heading and one prototype table per dataset, each already carrying
    /// the right {{RESULTS:key}} marker.
    ///
    /// <para>This exists because the feature's usability rests entirely on the author typing
    /// those keys correctly in Word. Handing them a document where the keys are already right,
    /// and where restyling a table restyles that section's output, removes the only step that
    /// reliably goes wrong.</para>
    /// </summary>
    public static byte[] BuildReportStarterTemplate(
        string reportName,
        IReadOnlyList<ReportTemplateSection> sections,
        bool rightToLeft = false,
        IReadOnlyList<ReportTemplateChart>? charts = null)
    {
        // Charts are grouped by the dataset they draw, so each marker lands under that section's
        // table. A chart whose dataset is unknown goes at the end rather than being dropped:
        // silently omitting it would make a configured chart simply never appear.
        var chartsByDataset = (charts ?? Array.Empty<ReportTemplateChart>())
            .Where(c => c.DatasetKey is not null)
            .GroupBy(c => c.DatasetKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var orphanCharts = (charts ?? Array.Empty<ReportTemplateChart>())
            .Where(c => c.DatasetKey is null)
            .ToList();

        void AppendCharts(StringBuilder target, string datasetKey)
        {
            if (!chartsByDataset.TryGetValue(datasetKey, out var forDataset))
                return;
            foreach (var chart in forDataset)
            {
                if (!string.IsNullOrWhiteSpace(chart.Title))
                    AppendSubtitle(target, chart.Title, rightToLeft);
                AppendMarker(target, $"{{{{CHART:{chart.Key}}}}}", rightToLeft);
            }
        }

        var body = new StringBuilder();
        AppendHeading(body, "{{REPORT_NAME}}", rightToLeft);
        AppendSubtitle(body, "Generated {{GENERATED_AT}}", rightToLeft);
        AppendSubtitle(body, "{{PARAMS}}", rightToLeft);

        if (sections.Count == 0)
        {
            // A report with no datasets yet still gets a usable document rather than an
            // empty one, so the author can see the shape before wiring anything up.
            AppendSubtitle(body, "Add a dataset to this report, then download this template again.", rightToLeft);
            return BuildDocx(body.ToString(), rightToLeft);
        }

        var childrenByParent = sections
            .Where(s => s.ParentKey is not null)
            .GroupBy(s => s.ParentKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var section in sections.Where(s => s.ParentKey is null))
        {
            if (!childrenByParent.TryGetValue(section.Key, out var children))
            {
                AppendSectionHeading(body,
                    $"{section.Title}  ({{{{ROW_COUNT:{section.Key}}}}} row(s))", rightToLeft);
                AppendPrototypeTable(body, $"{{{{RESULTS:{section.Key}}}}}", rightToLeft);
                AppendCharts(body, section.Key);
                continue;
            }

            // A dataset with children becomes a repeating region: everything between the two
            // markers is rendered once per row of this dataset. {{FIELD:COLUMN}} inside it is
            // that row's cell — the author replaces COLUMN with a real column name.
            AppendSectionHeading(body, section.Title, rightToLeft);
            AppendMarker(body, $"{{{{#EACH:{section.Key}}}}}", rightToLeft);
            AppendSubtitle(body,
                $"{section.Title}: {{{{FIELD:COLUMN}}}} — replace COLUMN with a column of this dataset",
                rightToLeft);

            foreach (var child in children)
            {
                AppendSubtitle(body, child.Title, rightToLeft);
                AppendPrototypeTable(body, $"{{{{RESULTS:{child.Key}}}}}", rightToLeft);
            }

            AppendMarker(body, "{{/EACH}}", rightToLeft);
            AppendCharts(body, section.Key);
        }

        foreach (var chart in orphanCharts)
        {
            if (!string.IsNullOrWhiteSpace(chart.Title))
                AppendSubtitle(body, chart.Title, rightToLeft);
            AppendMarker(body, $"{{{{CHART:{chart.Key}}}}}", rightToLeft);
        }

        return BuildDocx(body.ToString(), rightToLeft);
    }

    /// <summary>
    /// Run properties for one line of the starter. <c>&lt;w:rtl/&gt;</c> is what actually makes
    /// Arabic text lay out right to left; <c>&lt;w:bidi/&gt;</c> on the paragraph handles the
    /// paragraph itself. Both are needed, and both are carried into generated rows because the
    /// prototype row's formatting is cloned.
    /// </summary>
    private static string RunProps(bool bold, int halfPoints, string? color, bool rightToLeft)
    {
        var sb = new StringBuilder("<w:rPr>");
        if (bold) sb.Append("<w:b/><w:bCs/>");
        if (color is not null) sb.Append("<w:color w:val=\"").Append(color).Append("\"/>");
        sb.Append("<w:sz w:val=\"").Append(halfPoints).Append("\"/>")
          .Append("<w:szCs w:val=\"").Append(halfPoints).Append("\"/>");
        if (rightToLeft) sb.Append("<w:rtl/>");
        return sb.Append("</w:rPr>").ToString();
    }

    private static void AppendLine(
        StringBuilder body, string text, bool bold, int halfPoints, string? color,
        bool rightToLeft, int spacingBefore = 0)
    {
        var runProps = RunProps(bold, halfPoints, color, rightToLeft);
        body.Append("<w:p><w:pPr>");
        if (rightToLeft) body.Append("<w:bidi/>");
        if (spacingBefore > 0) body.Append("<w:spacing w:before=\"").Append(spacingBefore).Append("\"/>");
        body.Append(runProps).Append("</w:pPr><w:r>").Append(runProps)
            .Append("<w:t xml:space=\"preserve\">").Append(EscapeXml(text)).Append("</w:t></w:r></w:p>");
    }

    private static void AppendHeading(StringBuilder body, string text, bool rightToLeft = false) =>
        AppendLine(body, text, bold: true, halfPoints: 32, color: null, rightToLeft);

    private static void AppendSectionHeading(StringBuilder body, string text, bool rightToLeft = false) =>
        AppendLine(body, text, bold: true, halfPoints: 24, color: null, rightToLeft, spacingBefore: 240);

    /// <summary>A bare marker paragraph. Kept unstyled so it reads as machinery, not content.</summary>
    private static void AppendMarker(StringBuilder body, string text, bool rightToLeft = false) =>
        AppendLine(body, text, bold: false, halfPoints: 16, color: "999999", rightToLeft);

    private static void AppendSubtitle(StringBuilder body, string text, bool rightToLeft = false) =>
        AppendLine(body, text, bold: false, halfPoints: 16, color: "666666", rightToLeft);

    /// <summary>
    /// A two-row prototype table holding <paramref name="marker"/>: row 1 styles the header
    /// and row 2 (the marker row) styles the data rows. Restyling these two rows in Word
    /// restyles every exported row.
    /// </summary>
    private static void AppendPrototypeTable(StringBuilder body, string marker, bool rightToLeft = false)
    {
        body.Append("<w:tbl><w:tblPr>");
        // Column order runs right to left. Carried into the generated table for free: the
        // prototype's <w:tblPr> is copied wholesale by BuildTableFromPrototype.
        if (rightToLeft) body.Append("<w:bidiVisual/>");
        body.Append("<w:tblW w:w=\"5000\" w:type=\"pct\"/>")
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
            .Append("<w:tc><w:tcPr><w:shd w:val=\"clear\" w:fill=\"E7E7E7\"/></w:tcPr>");
        AppendLine(body, "Column Header", bold: true, halfPoints: 18, color: null, rightToLeft);
        body.Append("</w:tc></w:tr><w:tr><w:tc><w:tcPr/>");
        AppendLine(body, marker, bold: false, halfPoints: 18, color: null, rightToLeft);
        body.Append("</w:tc></w:tr></w:tbl><w:p/>");
    }

    /// <summary>Wraps a body fragment in the minimal valid .docx package.</summary>
    private static byte[] BuildDocx(string bodyXml, bool rightToLeft = false)
    {
        var documentXml =
            XmlDeclaration +
            $"<w:document xmlns:w=\"{WordNs}\"><w:body>" + bodyXml +
            // Landscape A4 with 0.5" margins so wide result tables get maximum room.
            "<w:sectPr><w:pgSz w:w=\"16838\" w:h=\"11906\" w:orient=\"landscape\"/>" +
            "<w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" w:header=\"720\" w:footer=\"720\"/>" +
            (rightToLeft ? "<w:bidi/>" : "") + "</w:sectPr>" +
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

    // ---------- template inspection ----------

    /// <summary>
    /// Reads back which markers a template actually uses, so the upload endpoint can tell the
    /// author about a mistyped key while they can still fix it — rather than letting the
    /// section silently render empty later.
    /// </summary>
    public static ReportTemplateInspection Inspect(byte[] docx)
    {
        var referenced = new List<string>();
        var duplicatesInOneTable = new List<string>();
        var hasUnkeyed = false;
        var hasTextBoxPlaceholder = false;

        using var ms = new MemoryStream(docx, writable: false);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            var isDocumentPart = name == "word/document.xml" ||
                (name.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".xml")) ||
                (name.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".xml"));
            if (!isDocumentPart)
                continue;

            string xml;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8, leaveOpen: false))
                xml = reader.ReadToEnd();

            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            }
            catch (System.Xml.XmlException)
            {
                continue; // an unreadable part tells us nothing; the export path handles it
            }

            foreach (var paragraph in doc.Descendants(W + "p"))
            {
                var text = ParagraphText(paragraph);
                if (!text.Contains("{{", StringComparison.Ordinal))
                    continue;

                if (paragraph.Ancestors(W + "txbxContent").Any())
                    hasTextBoxPlaceholder = true;

                foreach (Match match in ResultsMarkerRegex().Matches(text))
                {
                    if (!match.Groups[1].Success)
                    {
                        hasUnkeyed = true;
                        continue;
                    }

                    var key = match.Groups[1].Value;
                    if (!referenced.Contains(key, StringComparer.OrdinalIgnoreCase))
                        referenced.Add(key);
                }

                // A {{#EACH:key}} region references its dataset just as much as a table marker
                // does — it renders one block per row. Without this the parent of a
                // master/detail pair would be reported as unused.
                foreach (Match match in EachOpenRegex().Matches(text))
                {
                    var key = match.Groups[1].Value;
                    if (!referenced.Contains(key, StringComparer.OrdinalIgnoreCase))
                        referenced.Add(key);
                }
            }

            // A prototype table is replaced wholesale, so a second marker inside the same
            // table never renders. That is an authoring error rather than a preference.
            foreach (var table in doc.Descendants(W + "tbl"))
            {
                var keysHere = table.Descendants(W + "p")
                    .SelectMany(p => ResultsMarkerRegex().Matches(ParagraphText(p)).Cast<Match>())
                    .Select(m => m.Groups[1].Success ? m.Groups[1].Value : string.Empty)
                    .ToList();

                if (keysHere.Count > 1)
                {
                    foreach (var key in keysHere.Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!duplicatesInOneTable.Contains(key, StringComparer.OrdinalIgnoreCase))
                            duplicatesInOneTable.Add(key);
                    }
                }
            }
        }

        return new ReportTemplateInspection(referenced, hasUnkeyed, duplicatesInOneTable, hasTextBoxPlaceholder);
    }

    // ---------- template filling ----------

    private static byte[] FillTemplate(
        byte[] template,
        IReadOnlyList<ExportResultSet> results,
        bool includeHeaders,
        IReadOnlyDictionary<string, string> replacements,
        IReadOnlyList<ReportChartData> charts)
    {
        using var ms = new MemoryStream();
        ms.Write(template);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            // Charts are resolved before the document parts are rewritten, because placing one
            // needs a relationship id that has to be reserved against whatever the template
            // already uses.
            var placedCharts = PrepareCharts(zip, charts);

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

                // Regions first: cloning a {{#EACH}} body has to happen before the tables
                // inside it are injected, so each clone gets its own parent's rows.
                var processed = isMainDocument ? ExpandRegions(xml, results) : xml;

                // Charts next, so a marker cloned by a region becomes its own drawing.
                if (isMainDocument)
                    processed = InjectCharts(processed, placedCharts);

                // Tables are only injected into the main body; in headers/footers the
                // {{RESULTS}} marker is simply removed.
                processed = InjectResults(processed, results, includeHeaders, allowTable: isMainDocument);
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

    // ---------- {{CHART:key}} ----------

    /// <summary>A chart that has been written into the package and given a relationship id.</summary>
    private sealed record PlacedChart(ReportChartData Chart, string RelationshipId, int Number);

    /// <summary>
    /// Writes each chart as its own part, registers its content type and its relationship, and
    /// returns what the document needs to point at them.
    ///
    /// <para>Three package parts have to agree for Word to accept a chart, and only one of them
    /// is the chart itself: <c>[Content_Types].xml</c> must declare the part, and
    /// <c>word/_rels/document.xml.rels</c> must relate it to the document. Both are <b>merged</b>
    /// rather than written fresh — an uploaded template usually has its own relationships
    /// already, and replacing them would strip its images, styles and headers.</para>
    /// </summary>
    private static List<PlacedChart> PrepareCharts(ZipArchive zip, IReadOnlyList<ReportChartData> charts)
    {
        var placed = new List<PlacedChart>();
        var drawable = charts.Where(c => !c.IsEmpty).ToList();
        if (drawable.Count == 0)
            return placed;

        var relationships = ReadEntry(zip, "word/_rels/document.xml.rels")
            ?? XmlDeclaration +
               "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"></Relationships>";

        // Reserve ids above everything the template already uses, so nothing is displaced.
        var nextId = RelationshipIdRegex().Matches(relationships)
            .Select(m => int.TryParse(m.Groups[1].Value, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var newRelationships = new StringBuilder();
        var newOverrides = new StringBuilder();

        for (var i = 0; i < drawable.Count; i++)
        {
            var number = i + 1;
            var relationshipId = "rId" + nextId++;
            var partName = $"word/charts/chart{number}.xml";

            ReplaceEntry(zip, partName, ChartPartWriter.BuildChartXml(drawable[i], number));

            newRelationships.Append("<Relationship Id=\"").Append(relationshipId)
                .Append("\" Type=\"").Append(ChartPartWriter.ChartRelationshipType)
                .Append("\" Target=\"charts/chart").Append(number).Append(".xml\"/>");

            newOverrides.Append("<Override PartName=\"/").Append(partName)
                .Append("\" ContentType=\"").Append(ChartPartWriter.ChartContentType).Append("\"/>");

            placed.Add(new PlacedChart(drawable[i], relationshipId, number));
        }

        var closing = relationships.LastIndexOf("</Relationships>", StringComparison.Ordinal);
        if (closing >= 0)
        {
            ReplaceEntry(zip, "word/_rels/document.xml.rels",
                relationships[..closing] + newRelationships + relationships[closing..]);
        }

        var contentTypes = ReadEntry(zip, "[Content_Types].xml");
        if (contentTypes is not null)
        {
            var end = contentTypes.LastIndexOf("</Types>", StringComparison.Ordinal);
            if (end >= 0)
            {
                ReplaceEntry(zip, "[Content_Types].xml",
                    contentTypes[..end] + newOverrides + contentTypes[end..]);
            }
        }

        return placed;
    }

    /// <summary>
    /// Replaces each {{CHART:key}} marker with an inline drawing pointing at that chart's part.
    /// A marker naming a chart the report does not have is removed, matching how an unknown
    /// dataset key behaves.
    /// </summary>
    private static string InjectCharts(string xml, IReadOnlyList<PlacedChart> charts)
    {
        if (!xml.Contains("{{CHART", StringComparison.Ordinal))
            return xml;

        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var markers = doc.Descendants(W + "p")
            .Select(p => (Paragraph: p, Match: ChartMarkerRegex().Match(ParagraphText(p))))
            .Where(x => x.Match.Success)
            .ToList();
        if (markers.Count == 0)
            return xml;

        // Each drawing needs its own docPr id even when several show the same chart, which is
        // what happens when a marker sits inside a repeated region.
        var occurrence = 0;

        foreach (var (marker, match) in markers)
        {
            if (marker.Document != doc)
                continue;

            var key = match.Groups[1].Value;
            var placed = charts.FirstOrDefault(
                c => string.Equals(c.Chart.Key, key, StringComparison.OrdinalIgnoreCase));

            if (placed is null)
            {
                marker.ReplaceWith(new XElement(W + "p"));
                continue;
            }

            occurrence++;
            var drawing = XElement.Parse(
                $"<f xmlns:w=\"{WordNs}\">" +
                ChartPartWriter.BuildDrawingParagraph(placed.Chart, placed.RelationshipId, occurrence) +
                "</f>");
            marker.ReplaceWith(drawing.Elements().Cast<object>().ToArray());
        }

        return XmlDeclaration + doc.Root!.ToString(SaveOptions.DisableFormatting);
    }

    private static string? ReadEntry(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null)
            return null;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, leaveOpen: false);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Adds a part to an archive being built from scratch.
    ///
    /// <para>Deliberately does NOT look for an existing entry: a <see cref="ZipArchive"/> opened
    /// in <see cref="ZipArchiveMode.Create"/> throws on <c>GetEntry</c>, and that is the mode
    /// every template generator here uses. Replacing a part in an existing package is
    /// <see cref="ReplaceEntry"/>, which requires Update mode.</para>
    /// </summary>
    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>
    /// Writes a part into a package opened for update, replacing it when it already exists.
    /// Only valid in <see cref="ZipArchiveMode.Update"/>.
    /// </summary>
    private static void ReplaceEntry(ZipArchive zip, string path, string content)
    {
        zip.GetEntry(path)?.Delete();
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    [GeneratedRegex(@"\{\{CHART:\s*([A-Za-z0-9_]+)\}\}")]
    private static partial Regex ChartMarkerRegex();

    [GeneratedRegex(@"Id=""rId(\d+)""")]
    private static partial Regex RelationshipIdRegex();

    // ---------- {{RESULTS}} injection ----------

    /// <summary>
    /// Replaces every {{RESULTS}} / {{RESULTS:key}} marker with the matching rows. A marker
    /// inside a table turns that table into the styling prototype (see class docs); a marker
    /// in a plain paragraph gets the neutral built-in table.
    /// </summary>
    private static string InjectResults(
        string xml,
        IReadOnlyList<ExportResultSet> results,
        bool includeHeaders,
        bool allowTable)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var markers = doc.Descendants(W + "p")
            .Select(p => (Paragraph: p, Match: ResultsMarkerRegex().Match(ParagraphText(p))))
            .Where(x => x.Match.Success)
            .ToList();
        if (markers.Count == 0)
            return xml;

        foreach (var (marker, match) in markers)
        {
            if (marker.Document != doc)
                continue; // already detached by a previous replacement in the same table

            var section = ResolveSection(
                results,
                match.Groups[1].Success ? match.Groups[1].Value : null,
                match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : null);

            // An empty section means the marker names a dataset this report does not have.
            // Render nothing rather than failing the export — the template inspector reports
            // the mismatch at upload, where the author can still act on it.
            if (!allowTable || section.Count == 0)
            {
                // Cells must keep at least one paragraph, so blank the marker instead of removing it.
                marker.ReplaceWith(new XElement(W + "p"));
                continue;
            }

            var prototype = marker.Ancestors(W + "tbl").FirstOrDefault();
            if (prototype is not null)
            {
                prototype.ReplaceWith(BuildTableFromPrototype(prototype, marker, section, includeHeaders));
            }
            else
            {
                var fragment = XElement.Parse(
                    $"<f xmlns:w=\"{WordNs}\">" + BuildTableXml(section, includeHeaders) + "<w:p/></f>");
                marker.ReplaceWith(fragment.Elements().Cast<object>().ToArray());
            }
        }

        return XmlDeclaration + doc.Root!.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Which result sets one marker renders. A bare {{RESULTS}} keeps meaning "every set,
    /// appended" — combined scheduled tasks depend on that and existing per-query templates
    /// must keep working. {{RESULTS:key}} means exactly one named set, and an unknown key
    /// resolves to nothing.
    ///
    /// <para>A <c>#index</c> suffix narrows a detail dataset to the rows fetched for one parent
    /// row. Templates never contain it — the region expander writes it while cloning a
    /// {{#EACH}} body, so each clone's table renders only its own parent's rows.</para>
    /// </summary>
    private static IReadOnlyList<ExportResultSet> ResolveSection(
        IReadOnlyList<ExportResultSet> results,
        string? key,
        int? parentIndex)
    {
        if (key is null)
            return results;

        var match = results.FirstOrDefault(
            r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return Array.Empty<ExportResultSet>();

        if (parentIndex is null)
            return new[] { match };

        var rows = match.Rows
            .Where(row => row.TryGetValue(ReportDetail.ParentIndexColumn, out var value)
                          && value is int index && index == parentIndex.Value)
            .ToList();

        return new[] { match with { Rows = rows } };
    }

    [GeneratedRegex(@"\{\{RESULTS(?::\s*([A-Za-z0-9_]+))?(?:#(\d+))?\}\}")]
    private static partial Regex ResultsMarkerRegex();

    // ---------- {{#EACH}} regions (master/detail) ----------

    /// <summary>
    /// Expands every {{#EACH:key}} … {{/EACH}} region: the block between the markers is cloned
    /// once per row of <c>key</c>, with {{FIELD:Column}} resolved to that row's cell and any
    /// {{RESULTS:child}} inside narrowed to that row's child rows.
    ///
    /// <para>This is what turns "a box of customer details followed by that customer's
    /// purchases" into one section per customer. It runs <b>before</b> table injection, so the
    /// tables the clones contain are still ordinary prototype tables by the time
    /// <see cref="InjectResults"/> sees them and keep all their styling behaviour.</para>
    ///
    /// <para>The two markers must be siblings — a region is a run of whole paragraphs and
    /// tables, not part of one. Anything else is rejected by name rather than rendered wrongly.</para>
    /// </summary>
    private static string ExpandRegions(string xml, IReadOnlyList<ExportResultSet> results)
    {
        if (!xml.Contains("{{#EACH", StringComparison.Ordinal))
            return xml;

        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);

        while (true)
        {
            var open = doc.Descendants(W + "p")
                .FirstOrDefault(p => EachOpenRegex().IsMatch(ParagraphText(p)));
            if (open is null)
                break;

            var key = EachOpenRegex().Match(ParagraphText(open)).Groups[1].Value;

            var close = open.ElementsAfterSelf()
                .FirstOrDefault(e => e.Name == W + "p" && EachCloseRegex().IsMatch(ParagraphText(e)));
            if (close is null)
            {
                throw new DomainException(
                    $"Template error: {{{{#EACH:{key}}}}} has no matching {{{{/EACH}}}} " +
                    "at the same level of the document.");
            }

            var body = open.ElementsAfterSelf().TakeWhile(e => e != close).ToList();

            var set = results.FirstOrDefault(
                r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            var rows = set?.Rows ?? Array.Empty<IReadOnlyDictionary<string, object?>>();

            var clones = new List<XElement>();
            for (int index = 0; index < rows.Count; index++)
            {
                foreach (var node in body)
                {
                    var clone = new XElement(node);
                    ApplyRowToRegion(clone, rows[index], index);
                    clones.Add(clone);
                }
            }

            foreach (var node in body)
                node.Remove();
            close.Remove();

            // An empty dataset removes the region entirely rather than leaving its markers behind.
            if (clones.Count == 0)
                open.Remove();
            else
                open.ReplaceWith(clones.Cast<object>().ToArray());
        }

        return XmlDeclaration + doc.Root!.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Resolves one cloned region body against the parent row it belongs to.
    /// </summary>
    private static void ApplyRowToRegion(
        XElement node,
        IReadOnlyDictionary<string, object?> row,
        int index)
    {
        foreach (var paragraph in node.DescendantsAndSelf(W + "p").ToList())
        {
            var text = ParagraphText(paragraph);
            if (!text.Contains("{{", StringComparison.Ordinal))
                continue;

            var replaced = FieldRegex().Replace(text, match =>
            {
                row.TryGetValue(match.Groups[1].Value, out var value);
                return ResultFileExporter.FormatValue(value);
            });

            // Tag any child-table marker with this parent's index so it renders only those rows.
            replaced = ResultsMarkerRegex().Replace(replaced, match =>
                match.Groups[1].Success && !match.Groups[2].Success
                    ? $"{{{{RESULTS:{match.Groups[1].Value}#{index}}}}}"
                    : match.Value);

            if (replaced != text)
                SetParagraphText(paragraph, replaced);
        }
    }

    /// <summary>
    /// Rewrites a paragraph to hold exactly <paramref name="text"/>, keeping its paragraph
    /// properties and the first run's character formatting — the same compromise
    /// <see cref="ReplaceInParagraphs"/> makes, and for the same reason: Word splits typed text
    /// across runs, so the text can only be replaced as a whole.
    /// </summary>
    private static void SetParagraphText(XElement paragraph, string text)
    {
        var properties = paragraph.Element(W + "pPr");
        var runProperties = paragraph.Elements(W + "r")
            .Select(r => r.Element(W + "rPr"))
            .FirstOrDefault(rPr => rPr is not null);

        paragraph.Elements().Where(e => e.Name != W + "pPr").Remove();

        var run = new XElement(W + "r");
        if (runProperties is not null)
            run.Add(new XElement(runProperties));
        run.Add(new XElement(W + "t",
            new XAttribute(XNamespace.Xml + "space", "preserve"),
            StripInvalidXmlChars(text)));

        paragraph.Add(run);
        _ = properties; // kept in place above; named for clarity
    }

    [GeneratedRegex(@"\{\{#EACH:\s*([A-Za-z0-9_]+)\}\}")]
    private static partial Regex EachOpenRegex();

    [GeneratedRegex(@"\{\{/EACH\}\}")]
    private static partial Regex EachCloseRegex();

    [GeneratedRegex(@"\{\{FIELD:\s*([^}]+?)\s*\}\}")]
    private static partial Regex FieldRegex();

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

            // A paragraph holding a text box contains a NESTED <w:p> (inside <w:txbxContent>),
            // and ParagraphRegex is lazy — so the match above stopped at the inner </w:p> and
            // rebuilding from it would drop the outer paragraph's remaining content and closing
            // tag, producing a .docx Word refuses to open. Leave these alone: a placeholder
            // inside a text box simply is not substituted, which is visible and recoverable.
            // The template inspector flags it at upload so the author is not surprised.
            if (paragraph.Contains("<w:txbxContent", StringComparison.Ordinal))
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

    // Each of these matches an element with content, so each must reject the SELF-CLOSING form
    // of the same element. Word writes <w:p/> for every empty paragraph, and without the
    // negative lookbehind `[^>]*` swallows the trailing "/" — the empty element is then treated
    // as an opening tag and the lazy body runs on to the NEXT element's closing tag, which
    // rebuilds two paragraphs into one malformed one. A template with a blank line above a
    // placeholder is enough to trigger it, which is most of them.

    [GeneratedRegex("""<w:p(?:\s[^>]*?)?(?<!/)>.*?</w:p>""", RegexOptions.Singleline)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex("""<w:t(?:\s[^>]*?)?(?<!/)>(.*?)</w:t>""", RegexOptions.Singleline)]
    private static partial Regex TextRunRegex();

    [GeneratedRegex("""<w:pPr(?:\s[^>]*?)?(?<!/)>.*?</w:pPr>""", RegexOptions.Singleline)]
    private static partial Regex ParagraphPropsRegex();

    [GeneratedRegex("""<w:rPr(?:\s[^>]*?)?(?<!/)>.*?</w:rPr>""", RegexOptions.Singleline)]
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
