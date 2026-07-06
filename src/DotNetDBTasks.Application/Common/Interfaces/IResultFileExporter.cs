using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Serializes a query result set into a downloadable/exportable file in the requested
/// format (Excel/CSV/JSON). Used by scheduled tasks to write result files to disk.
/// </summary>
public interface IResultFileExporter
{
    byte[] Export(
        ExportFileFormat format,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string name);

    /// <summary>File extension for the format, without the leading dot (e.g. "xlsx").</summary>
    string GetExtension(ExportFileFormat format);
}
