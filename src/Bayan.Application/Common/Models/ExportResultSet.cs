namespace Bayan.Application.Common.Models;

/// <summary>
/// One result set as handed to the file exporters.
///
/// <para>Two callers pass several of these and mean different things by it, which is what
/// <see cref="Key"/> distinguishes. A <b>combined scheduled task</b> passes several
/// <i>unkeyed</i> sets meaning "append these into one table", so the header comes from the
/// first set and the sets should have compatible columns. A <b>report</b> passes several
/// <i>keyed</i> sets meaning "these are separate named sections", each addressed
/// independently by a <c>{{RESULTS:key}}</c> marker in the Word template and each free to
/// have a completely different shape.</para>
///
/// <para>Carrying the key on the set itself is what lets reports reuse the existing exporter
/// pipeline unchanged rather than needing a parallel one.</para>
/// </summary>
/// <param name="Columns">Column names, in order.</param>
/// <param name="Rows">The rows, each keyed by column name.</param>
/// <param name="Key">
/// The template token naming this section, e.g. <c>sales</c> for <c>{{RESULTS:sales}}</c>.
/// Null means unkeyed — the append-into-one-table behaviour that predates reports.
/// </param>
/// <param name="Title">Human-readable heading, used for the Excel sheet tab and section headings.</param>
public record ExportResultSet(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    string? Key = null,
    string? Title = null);
