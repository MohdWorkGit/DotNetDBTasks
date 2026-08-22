using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;

namespace Bayan.Infrastructure.Services;

/// <summary>
/// Generates a minimal but valid Office Open XML (.xlsx) workbook using only the built-in
/// <see cref="ZipArchive"/> — no third-party package, so it works inside the air-gapped
/// offline bundle. Worksheets are streamed row-by-row so large exports do not build one
/// giant in-memory string.
///
/// <para>Two shapes come in, and <see cref="ExportResultSet.Key"/> tells them apart. Unkeyed
/// sets are a <b>combined scheduled task</b>: they mean "append these into one sheet", so the
/// header comes from the first set and one worksheet is written. Keyed sets are a
/// <b>report</b>: each is a separate named section with its own columns, so each gets its own
/// worksheet named after it. Squashing a report into one sheet would put a six-column section
/// under a five-column section's header, which is the whole reason keys exist.</para>
/// </summary>
public class ExcelExporter : IExcelExporter
{
    private const string RootRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    public byte[] Export(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string sheetName) =>
        Export(new[] { new ExportResultSet(columns, rows) }, sheetName, includeHeaders: true);

    public byte[] Export(
        IReadOnlyList<ExportResultSet> results,
        string sheetName,
        bool includeHeaders)
    {
        // One sheet per set only when the caller named them. Anything else keeps the
        // append-into-one-sheet behaviour combined scheduled tasks depend on.
        var perSheet = results.Any(r => !string.IsNullOrEmpty(r.Key));

        var sheets = perSheet
            ? results.Select(r => (Name: SheetNameFor(r, sheetName), Sets: (IReadOnlyList<ExportResultSet>)new[] { r })).ToList()
            : new List<(string Name, IReadOnlyList<ExportResultSet> Sets)> { (sheetName, results) };

        var names = UniqueSheetNames(sheets.Select(s => s.Name).ToList());

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(zip, "[Content_Types].xml", BuildContentTypesXml(sheets.Count));
            WriteTextEntry(zip, "_rels/.rels", RootRelsXml);
            WriteTextEntry(zip, "xl/workbook.xml", BuildWorkbookXml(names));
            WriteTextEntry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRelsXml(sheets.Count));

            for (int i = 0; i < sheets.Count; i++)
                WriteSheet(zip, i + 1, sheets[i].Sets, includeHeaders);
        }
        return ms.ToArray();
    }

    /// <summary>The tab label for one section: its title, else its key, else the workbook name.</summary>
    private static string SheetNameFor(ExportResultSet set, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(set.Title))
            return set.Title!;
        if (!string.IsNullOrWhiteSpace(set.Key))
            return set.Key!;
        return fallback;
    }

    private static string BuildContentTypesXml(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
          .Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">")
          .Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>")
          .Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>")
          .Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");

        for (int i = 1; i <= sheetCount; i++)
        {
            sb.Append("<Override PartName=\"/xl/worksheets/sheet").Append(i)
              .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        return sb.Append("</Types>").ToString();
    }

    private static string BuildWorkbookRelsXml(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
          .Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

        for (int i = 1; i <= sheetCount; i++)
        {
            sb.Append("<Relationship Id=\"rId").Append(i)
              .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet")
              .Append(i).Append(".xml\"/>");
        }

        return sb.Append("</Relationships>").ToString();
    }

    private static string BuildWorkbookXml(IReadOnlyList<string> sheetNames)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
          .Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ")
          .Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");

        for (int i = 0; i < sheetNames.Count; i++)
        {
            sb.Append("<sheet name=\"").Append(EscapeXml(sheetNames[i]))
              .Append("\" sheetId=\"").Append(i + 1)
              .Append("\" r:id=\"rId").Append(i + 1).Append("\"/>");
        }

        return sb.Append("</sheets></workbook>").ToString();
    }

    private static void WriteTextEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteSheet(
        ZipArchive zip,
        int sheetNumber,
        IReadOnlyList<ExportResultSet> results,
        bool includeHeaders)
    {
        var entry = zip.CreateEntry($"xl/worksheets/sheet{sheetNumber}.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var w = new StreamWriter(stream, new UTF8Encoding(false));

        w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        w.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        int rowNumber = 1;
        if (includeHeaders && results.Count > 0)
        {
            var columns = results[0].Columns;
            w.Write("<row r=\"1\">");
            for (int c = 0; c < columns.Count; c++)
                WriteInlineStringCell(w, CellRef(c, 1), columns[c]);
            w.Write("</row>");
            rowNumber = 2;
        }

        foreach (var result in results)
        {
            foreach (var row in result.Rows)
            {
                w.Write("<row r=\"");
                w.Write(rowNumber);
                w.Write("\">");
                for (int c = 0; c < result.Columns.Count; c++)
                {
                    row.TryGetValue(result.Columns[c], out var value);
                    WriteValueCell(w, CellRef(c, rowNumber), value);
                }
                w.Write("</row>");
                rowNumber++;
            }
        }

        w.Write("</sheetData></worksheet>");
    }

    private static void WriteValueCell(StreamWriter w, string cellRef, object? value)
    {
        if (value is null)
        {
            w.Write("<c r=\"");
            w.Write(cellRef);
            w.Write("\"/>");
            return;
        }

        switch (value)
        {
            case bool b:
                w.Write("<c r=\"");
                w.Write(cellRef);
                w.Write("\" t=\"b\"><v>");
                w.Write(b ? '1' : '0');
                w.Write("</v></c>");
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal:
                w.Write("<c r=\"");
                w.Write(cellRef);
                w.Write("\"><v>");
                w.Write(Convert.ToString(value, CultureInfo.InvariantCulture));
                w.Write("</v></c>");
                return;
            case DateTime dt:
                WriteInlineStringCell(w, cellRef, dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                return;
            default:
                WriteInlineStringCell(w, cellRef, value.ToString() ?? string.Empty);
                return;
        }
    }

    private static void WriteInlineStringCell(StreamWriter w, string cellRef, string text)
    {
        w.Write("<c r=\"");
        w.Write(cellRef);
        w.Write("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
        w.Write(EscapeXml(text));
        w.Write("</t></is></c>");
    }

    /// <summary>Builds an A1-style cell reference from a zero-based column index and 1-based row.</summary>
    private static string CellRef(int columnIndex, int rowNumber)
    {
        Span<char> buffer = stackalloc char[3];
        int pos = buffer.Length;
        int n = columnIndex;
        do
        {
            buffer[--pos] = (char)('A' + n % 26);
            n = n / 26 - 1;
        } while (n >= 0);

        return string.Concat(buffer[pos..], rowNumber.ToString(CultureInfo.InvariantCulture));
    }

    private static string EscapeXml(string value) =>
        SecurityElement.Escape(StripInvalidXmlChars(value)) ?? string.Empty;

    /// <summary>
    /// Removes characters that are illegal in XML 1.0 (e.g. control chars in raw DB text),
    /// which would otherwise corrupt the workbook and make Excel refuse to open it.
    /// </summary>
    private static string StripInvalidXmlChars(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var hasInvalid = false;
        foreach (var ch in value)
        {
            if (!IsLegalXmlChar(ch)) { hasInvalid = true; break; }
        }
        if (!hasInvalid)
            return value;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (IsLegalXmlChar(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static bool IsLegalXmlChar(char ch) =>
        ch == '\t' || ch == '\n' || ch == '\r' ||
        (ch >= ' ' && ch <= '퟿') ||
        (ch >= '' && ch <= '�');

    /// <summary>
    /// Excel refuses to open a workbook whose sheet names collide, and the 31-character
    /// truncation below can create a collision out of two names that started different — so
    /// deduplication has to happen after truncation, not before.
    /// </summary>
    private static IReadOnlyList<string> UniqueSheetNames(IReadOnlyList<string> names)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(names.Count);

        foreach (var name in names)
        {
            var candidate = SanitizeSheetName(name);
            if (used.Add(candidate))
            {
                result.Add(candidate);
                continue;
            }

            for (int suffix = 2; ; suffix++)
            {
                var tag = $" ({suffix})";
                var trimmed = candidate.Length + tag.Length > 31
                    ? candidate[..(31 - tag.Length)]
                    : candidate;
                var attempt = trimmed + tag;
                if (used.Add(attempt))
                {
                    result.Add(attempt);
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>Excel sheet names cannot exceed 31 chars or contain : \ / ? * [ ].</summary>
    private static string SanitizeSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Sheet1";

        var cleaned = new string(name.Where(ch => !":\\/?*[]".Contains(ch)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return "Sheet1";

        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }
}
