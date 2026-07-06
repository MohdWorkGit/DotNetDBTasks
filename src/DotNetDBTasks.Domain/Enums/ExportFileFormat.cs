namespace DotNetDBTasks.Domain.Enums;

/// <summary>
/// File format used when a scheduled task exports a query result to disk.
/// </summary>
public enum ExportFileFormat
{
    Excel = 0,
    Csv = 1,
    Json = 2
}
