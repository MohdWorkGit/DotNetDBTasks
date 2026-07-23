using System.Globalization;
using System.Text;
using System.Text.Json;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Infrastructure.Services;

/// <summary>
/// Format-dispatching result exporter. Excel delegates to the existing
/// <see cref="IExcelExporter"/> and PDF to <see cref="PdfExporter"/>; CSV and JSON are
/// generated here with only the base class library, keeping the air-gapped offline
/// bundle dependency-free.
/// Several result sets can be written into one file (combined scheduled-task
/// output); the header row, when enabled, comes from the first set.
/// </summary>
public class ResultFileExporter : IResultFileExporter
{
    private readonly IExcelExporter _excelExporter;
    private readonly IDocxToPdfConverter _docxToPdfConverter;

    public ResultFileExporter(IExcelExporter excelExporter, IDocxToPdfConverter docxToPdfConverter)
    {
        _excelExporter = excelExporter;
        _docxToPdfConverter = docxToPdfConverter;
    }

    public byte[] Export(
        ExportFileFormat format,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string name,
        string csvSeparator = ",",
        bool includeHeaders = true,
        byte[]? wordTemplate = null,
        IReadOnlyList<ExportParameter>? parameters = null) =>
        Export(format, new[] { new ExportResultSet(columns, rows) }, name, csvSeparator, includeHeaders, wordTemplate, parameters);

    public byte[] Export(
        ExportFileFormat format,
        IReadOnlyList<ExportResultSet> results,
        string name,
        string csvSeparator = ",",
        bool includeHeaders = true,
        byte[]? wordTemplate = null,
        IReadOnlyList<ExportParameter>? parameters = null)
    {
        return format switch
        {
            ExportFileFormat.Excel => _excelExporter.Export(results, name, includeHeaders),
            ExportFileFormat.Csv => ExportCsv(results, csvSeparator, includeHeaders),
            ExportFileFormat.Json => ExportJson(results),
            ExportFileFormat.Pdf => ExportPdf(results, name, includeHeaders, wordTemplate, parameters),
            ExportFileFormat.Word => WordExporter.Export(results, name, includeHeaders, wordTemplate, parameters),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.")
        };
    }

    public string GetExtension(ExportFileFormat format) => format switch
    {
        ExportFileFormat.Excel => "xlsx",
        ExportFileFormat.Csv => "csv",
        ExportFileFormat.Json => "json",
        ExportFileFormat.Pdf => "pdf",
        ExportFileFormat.Word => "docx",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.")
    };

    public byte[] GetStarterWordTemplate() => WordExporter.BuildStarterTemplate();

    /// <summary>
    /// PDF export: when a DOCX->PDF engine is installed, the PDF is the Word-template
    /// output converted — so it carries the same template styling as the Word export
    /// (per-query template, system default, or built-in starter). Without an engine, or
    /// when conversion fails, the built-in table layout is used.
    /// </summary>
    private byte[] ExportPdf(
        IReadOnlyList<ExportResultSet> results,
        string name,
        bool includeHeaders,
        byte[]? wordTemplate,
        IReadOnlyList<ExportParameter>? parameters)
    {
        if (_docxToPdfConverter.IsAvailable)
        {
            var docx = WordExporter.Export(results, name, includeHeaders, wordTemplate, parameters);
            var pdf = _docxToPdfConverter.TryConvert(docx);
            if (pdf is not null)
                return pdf;
        }
        return PdfExporter.Export(results, name, includeHeaders);
    }

    /// <summary>RFC 4180-style CSV, UTF-8 with BOM so Excel detects the encoding when opening it.</summary>
    private static byte[] ExportCsv(
        IReadOnlyList<ExportResultSet> results,
        string separator,
        bool includeHeaders)
    {
        using var ms = new MemoryStream();
        using (var w = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
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

    internal static string FormatValue(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        // Oracle RAW columns (e.g. GUID keys) come back as byte[]; hex beats "System.Byte[]".
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

    /// <summary>JSON array of objects, one per row across all result sets, preserving column order.</summary>
    private static byte[] ExportJson(IReadOnlyList<ExportResultSet> results)
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
}
