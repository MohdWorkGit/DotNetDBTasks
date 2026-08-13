namespace Bayan.Application.Common.Models;

/// <summary>
/// One query's result set, as handed to the file exporters. Combined scheduled-task
/// exports pass several of these; the rows are written in list order and the header
/// (when enabled) comes from the first set's columns.
/// </summary>
public record ExportResultSet(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);
