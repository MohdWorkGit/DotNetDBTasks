namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Builds an Excel (.xlsx) workbook from a tabular result set.
/// </summary>
public interface IExcelExporter
{
    /// <summary>
    /// Produces a single-sheet .xlsx file with the given columns as a header row followed
    /// by one row per record. Numeric and boolean values are written as native Excel types;
    /// everything else is written as text.
    /// </summary>
    byte[] Export(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string sheetName);
}
