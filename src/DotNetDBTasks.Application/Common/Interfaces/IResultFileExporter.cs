using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Serializes query result sets into a downloadable/exportable file in the requested
/// format (Excel/CSV/JSON/PDF/Word). Used by scheduled tasks to write result files to
/// disk and by the on-demand result download.
/// </summary>
public interface IResultFileExporter
{
    /// <param name="csvSeparator">CSV only: the field separator text (default comma), e.g. ";" or ";;".</param>
    /// <param name="includeHeaders">CSV/Excel/PDF/Word only: include the column header row (default true).</param>
    /// <param name="wordTemplate">Word only: .docx template whose placeholders are filled;
    /// null uses the built-in default document layout.</param>
    /// <param name="parameters">Word/PDF only: the parameters the query ran with (name, display
    /// name and value), exposed in the template as {{@name}} placeholders and the {{PARAMS}}
    /// "Display Name: value" summary.</param>
    byte[] Export(
        ExportFileFormat format,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string name,
        string csvSeparator = ",",
        bool includeHeaders = true,
        byte[]? wordTemplate = null,
        IReadOnlyList<ExportParameter>? parameters = null);

    /// <summary>
    /// Writes several result sets into ONE file, in list order (combined scheduled-task
    /// output). The header row, when enabled, comes from the first set's columns, so the
    /// sets should have compatible columns.
    /// </summary>
    byte[] Export(
        ExportFileFormat format,
        IReadOnlyList<ExportResultSet> results,
        string name,
        string csvSeparator = ",",
        bool includeHeaders = true,
        byte[]? wordTemplate = null,
        IReadOnlyList<ExportParameter>? parameters = null);

    /// <summary>File extension for the format, without the leading dot (e.g. "xlsx").</summary>
    string GetExtension(ExportFileFormat format);

    /// <summary>
    /// The built-in starter Word template (.docx with {{RESULTS}} etc. placeholders) —
    /// the layout Word exports use when neither a per-query nor a system default
    /// template exists. Admins download it as the starting point for their own default.
    /// </summary>
    byte[] GetStarterWordTemplate();
}
