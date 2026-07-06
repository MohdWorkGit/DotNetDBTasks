using System.Globalization;
using System.Text;
using System.Text.Json;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Infrastructure.Services;

/// <summary>
/// Format-dispatching result exporter. Excel delegates to the existing
/// <see cref="IExcelExporter"/>; CSV and JSON are generated here with only the
/// base class library, keeping the air-gapped offline bundle dependency-free.
/// </summary>
public class ResultFileExporter : IResultFileExporter
{
    private readonly IExcelExporter _excelExporter;

    public ResultFileExporter(IExcelExporter excelExporter)
    {
        _excelExporter = excelExporter;
    }

    public byte[] Export(
        ExportFileFormat format,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string name)
    {
        return format switch
        {
            ExportFileFormat.Excel => _excelExporter.Export(columns, rows, name),
            ExportFileFormat.Csv => ExportCsv(columns, rows),
            ExportFileFormat.Json => ExportJson(columns, rows),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.")
        };
    }

    public string GetExtension(ExportFileFormat format) => format switch
    {
        ExportFileFormat.Excel => "xlsx",
        ExportFileFormat.Csv => "csv",
        ExportFileFormat.Json => "json",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.")
    };

    /// <summary>RFC 4180 CSV, UTF-8 with BOM so Excel detects the encoding when opening it.</summary>
    private static byte[] ExportCsv(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        using var ms = new MemoryStream();
        using (var w = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        {
            WriteCsvRow(w, columns.Select(c => (object?)c));
            foreach (var row in rows)
            {
                WriteCsvRow(w, columns.Select(c => row.TryGetValue(c, out var v) ? v : null));
            }
        }
        return ms.ToArray();
    }

    private static void WriteCsvRow(StreamWriter w, IEnumerable<object?> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
                w.Write(',');
            first = false;
            w.Write(EscapeCsv(FormatValue(value)));
        }
        w.Write("\r\n");
    }

    private static string FormatValue(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        // Oracle RAW columns (e.g. GUID keys) come back as byte[]; hex beats "System.Byte[]".
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>JSON array of objects, one per row, preserving column order.</summary>
    private static byte[] ExportJson(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();
            foreach (var row in rows)
            {
                writer.WriteStartObject();
                foreach (var column in columns)
                {
                    row.TryGetValue(column, out var value);
                    WriteJsonValue(writer, column, value);
                }
                writer.WriteEndObject();
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
