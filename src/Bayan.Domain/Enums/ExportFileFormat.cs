namespace Bayan.Domain.Enums;

/// <summary>
/// File format used when a query result is exported — scheduled-task files on disk
/// and on-demand result downloads.
/// </summary>
public enum ExportFileFormat
{
    Excel = 0,
    Csv = 1,
    Json = 2,
    Pdf = 3,
    Word = 4
}
