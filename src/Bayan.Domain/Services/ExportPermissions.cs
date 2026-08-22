using Bayan.Domain.Constants;
using Bayan.Domain.Enums;

namespace Bayan.Domain.Services;

/// <summary>
/// The rules deciding whether a result may be downloaded in a given format.
///
/// <para>Two independent gates have to agree. The <b>subject</b> — a query
/// (<see cref="Entities.DynamicQuery.AllowedExportFormats"/>) or a report
/// (<see cref="Entities.Report.AllowedExportFormats"/>) — lists the formats it may ever be
/// exported as, and the <b>role</b> says which formats this person may use at all. A download
/// needs both; either alone grants nothing. Keeping the mapping here rather than at the call
/// sites means the API and the client cannot drift into disagreeing about what is allowed.</para>
///
/// <para>Queries and reports carry separate permission families (<c>queries.export*</c> vs
/// <c>reports.export*</c>) rather than sharing one, because being trusted to download a single
/// query's rows is not the same decision as being trusted to download a whole assembled
/// document.</para>
/// </summary>
public static class ExportPermissions
{
    /// <summary>The permission a role must hold to download a result in this format.</summary>
    public static string PermissionFor(ExportFileFormat format) => format switch
    {
        ExportFileFormat.Excel => Permissions.QueriesExportExcel,
        ExportFileFormat.Csv => Permissions.QueriesExportCsv,
        ExportFileFormat.Json => Permissions.QueriesExportJson,
        ExportFileFormat.Pdf => Permissions.QueriesExportPdf,
        ExportFileFormat.Word => Permissions.QueriesExportWord,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format.")
    };

    /// <summary>The permission a role must hold to download a <i>report</i> in this format.</summary>
    public static string ReportPermissionFor(ExportFileFormat format) => format switch
    {
        ExportFileFormat.Excel => Permissions.ReportsExportExcel,
        ExportFileFormat.Csv => Permissions.ReportsExportCsv,
        ExportFileFormat.Json => Permissions.ReportsExportJson,
        ExportFileFormat.Pdf => Permissions.ReportsExportPdf,
        ExportFileFormat.Word => Permissions.ReportsExportWord,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format.")
    };

    /// <summary>
    /// Parses the stored comma-separated list. Unknown names are dropped rather than throwing:
    /// a value written by an older or newer build must not make an existing query unopenable.
    /// </summary>
    public static IReadOnlyList<ExportFileFormat> Parse(string? allowedExportFormats)
    {
        if (string.IsNullOrWhiteSpace(allowedExportFormats))
            return Array.Empty<ExportFileFormat>();

        var formats = new List<ExportFileFormat>();
        foreach (var part in allowedExportFormats.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse<ExportFileFormat>(part.Trim(), ignoreCase: true, out var format)
                && !formats.Contains(format))
            {
                formats.Add(format);
            }
        }

        return formats;
    }

    /// <summary>
    /// Renders the list for storage. Returns null for an empty set so "no export" is a NULL
    /// column rather than an empty string — Oracle treats the two as the same value, and a
    /// nullable column says what is meant.
    /// </summary>
    public static string? Serialize(IEnumerable<ExportFileFormat>? formats)
    {
        if (formats is null)
            return null;

        var distinct = formats.Distinct().OrderBy(f => (int)f).ToList();
        return distinct.Count == 0 ? null : string.Join(',', distinct.Select(f => f.ToString()));
    }

    /// <summary>
    /// The formats this caller may actually download for this query: what the query permits,
    /// narrowed to what the caller's roles permit. This is what the client should render its
    /// export menu from, and what the API enforces.
    /// </summary>
    public static IReadOnlyList<ExportFileFormat> Effective(
        string? allowedExportFormats,
        Func<string, bool> holdsPermission)
    {
        return Parse(allowedExportFormats)
            .Where(f => holdsPermission(PermissionFor(f)))
            .ToList();
    }

    /// <summary>
    /// The formats this caller may actually download for this <i>report</i>: what the report
    /// permits, narrowed to what the caller's roles permit through the <c>reports.export*</c>
    /// family.
    /// </summary>
    public static IReadOnlyList<ExportFileFormat> EffectiveForReport(
        string? allowedExportFormats,
        Func<string, bool> holdsPermission)
    {
        return Parse(allowedExportFormats)
            .Where(f => holdsPermission(ReportPermissionFor(f)))
            .ToList();
    }
}
