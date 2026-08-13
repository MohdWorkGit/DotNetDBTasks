using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;

namespace Bayan.QueryRunner;

public enum ExportFormat
{
    Excel,
    Csv,
    Json
}

/// <summary>One executed query's result set, carrying its own column list.</summary>
public record QueryResult(
    string QueryName,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

/// <summary>
/// Self-contained result-set writers (this tool is deliberately independent of the
/// web application's projects). Excel is a minimal but valid Office Open XML (.xlsx)
/// workbook built with only the BCL <see cref="ZipArchive"/>; CSV is RFC 4180 UTF-8
/// with BOM; JSON is an array of row objects. Output matches the web app's exports.
///
/// All result sets are written into one file, in list order. When headers are enabled
/// the header row comes from the first result set (append compatible queries only).
/// </summary>
public static class FileExporter
{
    /// <param name="emitBom">
    /// CSV only: write the UTF-8 BOM at the start. Pass false when the bytes will be
    /// appended to an existing file (a BOM belongs only at the very beginning).
    /// </param>
    /// <param name="separator">CSV only: the field separator text (default comma), e.g. ";" or ";;".</param>
    public static byte[] Export(
        ExportFormat format,
        IReadOnlyList<QueryResult> results,
        string name,
        bool includeHeaders,
        bool emitBom = true,
        string separator = ",") => format switch
    {
        ExportFormat.Excel => ExportExcel(results, name, includeHeaders),
        ExportFormat.Csv => ExportCsv(results, includeHeaders, emitBom, separator),
        ExportFormat.Json => ExportJson(results),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string GetExtension(ExportFormat format) => format switch
    {
        ExportFormat.Excel => "xlsx",
        ExportFormat.Csv => "csv",
        ExportFormat.Json => "json",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    // ---------------------------------------------------------------- CSV

    private static byte[] ExportCsv(IReadOnlyList<QueryResult> results, bool includeHeaders, bool emitBom, string separator)
    {
        using var ms = new MemoryStream();
        using (var w = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: emitBom), leaveOpen: true))
        {
            if (includeHeaders && results.Count > 0)
                WriteCsvRow(w, results[0].Columns.Select(c => (object?)c), separator);

            foreach (var result in results)
            {
                foreach (var row in result.Rows)
                    WriteCsvRow(w, result.Columns.Select(c => row.TryGetValue(c, out var v) ? v : null), separator);
            }
        }
        return ms.ToArray();
    }

    private static void WriteCsvRow(StreamWriter w, IEnumerable<object?> values, string separator)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
                w.Write(separator);
            first = false;
            w.Write(EscapeCsv(FormatValue(value), separator));
        }
        w.Write("\r\n");
    }

    /// <summary>Culture-invariant string form of a DB value (also used to persist checkpoint keys).</summary>
    public static string FormatValue(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>
    /// Quotes a value containing quote/line-break characters or any character of the
    /// separator. Checking per character (not the full separator string) keeps
    /// multi-character separators unambiguous — e.g. with ";;" a value ending in ";"
    /// followed by a field starting with ";" would otherwise fabricate a separator.
    /// </summary>
    private static string EscapeCsv(string value, string separator)
    {
        if (value.IndexOfAny(new[] { '"', '\r', '\n' }) < 0 && value.IndexOfAny(separator.ToCharArray()) < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    // ---------------------------------------------------------------- JSON

    private static byte[] ExportJson(IReadOnlyList<QueryResult> results)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();
            foreach (var result in results)
            {
                foreach (var row in result.Rows)
                {
                    writer.WriteStartObject();
                    foreach (var column in result.Columns)
                    {
                        row.TryGetValue(column, out var value);
                        WriteJsonValue(writer, column, value);
                    }
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }
        return ms.ToArray();
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                writer.WriteNull(name);
                break;
            case bool b:
                writer.WriteBoolean(name, b);
                break;
            case byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal:
                writer.WriteNumber(name, Convert.ToDecimal(value, CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                writer.WriteString(name, dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                break;
            case byte[] bytes:
                writer.WriteString(name, Convert.ToHexString(bytes));
                break;
            default:
                writer.WriteString(name, value.ToString());
                break;
        }
    }

    // ---------------------------------------------------------------- Excel

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

    private static byte[] ExportExcel(
        IReadOnlyList<QueryResult> results,
        string sheetName,
        bool includeHeaders)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(zip, "[Content_Types].xml", ContentTypesXml);
            WriteTextEntry(zip, "_rels/.rels", RootRelsXml);
            WriteTextEntry(zip, "xl/workbook.xml", BuildWorkbookXml(sheetName));
            WriteTextEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
            WriteSheet(zip, results, includeHeaders);
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

    private static void WriteSheet(ZipArchive zip, IReadOnlyList<QueryResult> results, bool includeHeaders)
    {
        var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
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
        if (value is null or DBNull)
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
            case byte[] bytes:
                WriteInlineStringCell(w, cellRef, Convert.ToHexString(bytes));
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
