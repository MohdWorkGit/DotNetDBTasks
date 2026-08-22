namespace Bayan.Application.Common.Models;

/// <summary>
/// One dataset as the starter-template generator needs to know it: the key a marker will
/// address it by, the heading to print above its table, and — for a master/detail child — the
/// key of the parent it repeats under.
/// </summary>
/// <param name="Key">The token a {{RESULTS:key}} marker addresses this dataset by.</param>
/// <param name="Title">Heading printed above the table.</param>
/// <param name="ParentKey">
/// Set when this dataset repeats per row of another. The generator then nests its table inside
/// the parent's {{#EACH}} region instead of giving it a section of its own.
/// </param>
public sealed record ReportTemplateSection(string Key, string Title, string? ParentKey = null);

/// <summary>
/// A chart, as the starter-template generator needs it: the token to write, the heading to put
/// above it, and the dataset it belongs beside so the marker lands under the right section.
/// </summary>
public sealed record ReportTemplateChart(string Key, string Title, string? DatasetKey = null);

/// <summary>
/// What a report's uploaded Word template actually references, read back out of the .docx.
///
/// <para>The whole feature's usability rests on the author getting <c>{{RESULTS:key}}</c>
/// right in Word, and a mistyped key is otherwise invisible: the section simply renders
/// empty, hours later, in a document nobody re-reads. Inspecting at upload turns that into
/// an immediate, specific message.</para>
/// </summary>
/// <param name="ReferencedDatasetKeys">Keys named by <c>{{RESULTS:key}}</c> markers, deduplicated.</param>
/// <param name="HasUnkeyedResultsMarker">
/// True when a bare <c>{{RESULTS}}</c> is present. Valid, but in a multi-dataset report it
/// appends every dataset into one table, which is rarely what the author meant.
/// </param>
/// <param name="DuplicateKeysInOneTable">
/// Keys marked more than once inside a single prototype table. Only the first is rendered —
/// the table is replaced wholesale — so this is an authoring error, not a preference.
/// </param>
/// <param name="HasPlaceholderInsideTextBox">
/// True when a placeholder sits inside a Word text box, where it cannot be substituted.
/// See the note in <c>WordExporter.ReplaceInParagraphs</c>.
/// </param>
public sealed record ReportTemplateInspection(
    IReadOnlyList<string> ReferencedDatasetKeys,
    bool HasUnkeyedResultsMarker,
    IReadOnlyList<string> DuplicateKeysInOneTable,
    bool HasPlaceholderInsideTextBox);
