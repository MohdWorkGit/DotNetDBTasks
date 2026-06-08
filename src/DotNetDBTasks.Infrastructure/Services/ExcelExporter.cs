using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using DotNetDBTasks.Application.Common.Interfaces;

namespace DotNetDBTasks.Infrastructure.Services;

/// <summary>
/// Generates a minimal but valid Office Open XML (.xlsx) workbook using only the built-in
/// <see cref="ZipArchive"/> — no third-party package, so it works inside the air-gapped
/// offline bundle. The worksheet is streamed row-by-row so large exports do not build one
/// giant in-memory string.
/// </summary>
public class ExcelExporter : IExcelExporter
{
    private const string ContentTypesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "</Types>";

    private const string RootRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string WorkbookRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "</Relationships>";

    public byte[] Export(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string sheetName)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(zip, "[Content_Types].xml", ContentTypesXml);
            WriteTextEntry(zip, "_rels/.rels", RootRelsXml);
            WriteTextEntry(zip, "xl/workbook.xml", BuildWorkbookXml(sheetName));
            WriteTextEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
            WriteSheet(zip, columns, rows);
        }
        return ms.ToArray();
    }

    private static string BuildWorkbookXml(string sheetName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        "<sheets><sheet name=\"" + EscapeXml(SanitizeSheetName(sheetName)) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
        "</workbook>";

    private static void WriteTextEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteSheet(
        ZipArchive zip,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var w = new StreamWriter(stream, new UTF8Encoding(false));

        w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        w.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        // Header row
        w.Write("<row r=\"1\">");
        for (int c = 0; c < columns.Count; c++)
            WriteInlineStringCell(w, CellRef(c, 1), columns[c]);
        w.Write("</row>");

        // Data rows
        int rowNumber = 2;
        foreach (var row in rows)
        {
            w.Write("<row r=\"");
            w.Write(rowNumber);
            w.Write("\">");
            for (int c = 0; c < columns.Count; c++)
            {
                row.TryGetValue(columns[c], out var value);
                WriteValueCell(w, CellRef(c, rowNumber), value);
            }
            w.Write("</row>");
            rowNumber++;
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

    /// <summary>Excel sheet names cannot exceed 31 chars or contain : \ / ? * [ ].</summary>
    private static string SanitizeSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Sheet1";

        var cleaned = new string(name.Where(ch => !":\\/?*[]".Contains(ch)).ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
            return "Sheet1";

        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }
}
