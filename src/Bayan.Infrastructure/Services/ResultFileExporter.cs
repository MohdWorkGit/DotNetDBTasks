using System.Globalization;
using System.Text;
using System.Text.Json;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;
using Bayan.Domain.Enums;

namespace Bayan.Infrastructure.Services;

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
        IReadOnlyList<ExportParameter>? parameters = null,
        IReadOnlyList<ReportChartData>? charts = null)
    {
        return format switch
        {
            ExportFileFormat.Excel => _excelExporter.Export(results, name, includeHeaders),
            ExportFileFormat.Csv => ExportCsv(results, csvSeparator, includeHeaders),
            ExportFileFormat.Json => ExportJson(results),
            ExportFileFormat.Pdf => ExportPdf(results, name, includeHeaders, wordTemplate, parameters, charts),
            ExportFileFormat.Word => WordExporter.Export(results, name, includeHeaders, wordTemplate, parameters, charts),
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

    public byte[] GetReportStarterTemplate(
        string reportName,
        IReadOnlyList<ReportTemplateSection> sections,
        bool rightToLeft = false,
        IReadOnlyList<ReportTemplateChart>? charts = null) =>
        WordExporter.BuildReportStarterTemplate(reportName, sections, rightToLeft, charts);

    public ReportTemplateInspection InspectWordTemplate(byte[] docx) => WordExporter.Inspect(docx);

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
        IReadOnlyList<ExportParameter>? parameters,
        IReadOnlyList<ReportChartData>? charts = null)
    {
        if (_docxToPdfConverter.IsAvailable)
        {
            var docx = WordExporter.Export(results, name, includeHeaders, wordTemplate, parameters, charts);
            var pdf = _docxToPdfConverter.TryConvert(docx);
            if (pdf is not null)
                return pdf;
        }
        return PdfExporter.Export(results, name, includeHeaders);
    }

    /// <summary>
    /// RFC 4180-style CSV, UTF-8 with BOM so Excel detects the encoding when opening it.
    ///
    /// <para>Unkeyed sets are appended under one header — the combined scheduled-task shape.
    /// Keyed sets are report sections with different columns, so each is written as its own
    /// block preceded by a blank line and its title, and each gets its own header row. One
    /// shared header would be wrong for every section but the first.</para>
    /// </summary>
    private static byte[] ExportCsv(
        IReadOnlyList<ExportResultSet> results,
        string separator,
        bool includeHeaders)
    {
        using var ms = new MemoryStream();
        using (var w = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        {
            // Only a report names its sets. Unnamed sets are an ordinary query export: one
            // block, one header, no section labels.
            var perSection = results.Any(r => !string.IsNullOrEmpty(r.Key));

            if (!perSection)
            {
                if (includeHeaders && results.Count > 0)
                    WriteCsvRow(w, results[0].Columns.Select(c => (object?)c), separator);

                foreach (var result in results)
                {
                    foreach (var row in result.Rows)
                        WriteCsvRow(w, result.Columns.Select(c => row.TryGetValue(c, out var v) ? v : null), separator);
                }
            }
            else
            {
                var first = true;
                foreach (var result in results)
                {
                    // Blank line between sections so the blocks are visually separable.
                    if (!first)
                        w.Write("\r\n");
                    first = false;

                    var label = string.IsNullOrWhiteSpace(result.Title) ? result.Key : result.Title;
                    if (!string.IsNullOrWhiteSpace(label))
                        WriteCsvRow(w, new object?[] { label }, separator);

                    if (includeHeaders)
                        WriteCsvRow(w, result.Columns.Select(c => (object?)c), separator);

                    foreach (var row in result.Rows)
                        WriteCsvRow(w, result.Columns.Select(c => row.TryGetValue(c, out var v) ? v : null), separator);
                }
            }
        }
        // Read only after the writer is disposed. StreamWriter buffers, so taking the bytes
        // while it is still open returns whatever happened to have been flushed — for a short
        // result, nothing at all.
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

    /// <summary>
    /// Unkeyed sets produce a flat JSON array of row objects, preserving column order — the
    /// shape query and combined scheduled-task downloads have always had.
    ///
    /// <para>Keyed sets produce an <i>object</i> keyed by dataset instead, because flattening a
    /// report's sections into one array would interleave rows of different shapes with no way to
    /// tell which section any of them came from.</para>
    /// </summary>
    private static byte[] ExportJson(IReadOnlyList<ExportResultSet> results)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            var perSection = results.Any(r => !string.IsNullOrEmpty(r.Key));

            if (perSection)
            {
                writer.WriteStartObject();
                foreach (var result in results)
                {
                    writer.WritePropertyName(result.Key!);
                    WriteJsonRows(writer, result);
                }
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteStartArray();
                foreach (var result in results)
                {
                    foreach (var row in result.Rows)
                        WriteJsonRow(writer, result, row);
                }
                writer.WriteEndArray();
            }
        }
        return ms.ToArray();
    }

    private static void WriteJsonRows(Utf8JsonWriter writer, ExportResultSet result)
    {
        writer.WriteStartArray();
        foreach (var row in result.Rows)
            WriteJsonRow(writer, result, row);
        writer.WriteEndArray();
    }

    private static void WriteJsonRow(
        Utf8JsonWriter writer,
        ExportResultSet result,
        IReadOnlyDictionary<string, object?> row)
    {
        writer.WriteStartObject();
        foreach (var column in result.Columns)
        {
            row.TryGetValue(column, out var value);
            WriteJsonValue(writer, column, value);
        }
        writer.WriteEndObject();
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
