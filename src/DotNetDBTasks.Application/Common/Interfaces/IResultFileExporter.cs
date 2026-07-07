using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Serializes query result sets into a downloadable/exportable file in the requested
/// format (Excel/CSV/JSON). Used by scheduled tasks to write result files to disk.
/// </summary>
public interface IResultFileExporter
{
    /// <param name="csvSeparator">CSV only: the field separator text (default comma), e.g. ";" or ";;".</param>
    /// <param name="includeHeaders">CSV/Excel only: include the column header row (default true).</param>
    byte[] Export(
        ExportFileFormat format,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string name,
        string csvSeparator = ",",
        bool includeHeaders = true);

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
        bool includeHeaders = true);

    /// <summary>File extension for the format, without the leading dot (e.g. "xlsx").</summary>
    string GetExtension(ExportFileFormat format);
}
