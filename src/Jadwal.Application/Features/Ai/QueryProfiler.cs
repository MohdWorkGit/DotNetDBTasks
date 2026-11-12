using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Common.Reporting;
using Jadwal.Application.Features.QueryExecution.Commands;
using Jadwal.Domain.Entities;
using Jadwal.Domain.Enums;
using Jadwal.Domain.Exceptions;
using Jadwal.Domain.Interfaces;
using MediatR;

namespace Jadwal.Application.Features.Ai;

/// <summary>One result column as the model sees it: its name and what kind of values it holds.</summary>
internal sealed record ColumnProfile(string Name, string Kind, int Distinct, bool HasNulls)
{
    public const string Number = "number";
    public const string Date = "date";
    public const string Text = "text";
    public const string Boolean = "boolean";
}

/// <summary>A saved query described by its result's shape: no values, only names, kinds and counts.</summary>
/// <param name="AssumedValues">
/// Values chosen for required parameters that have no default, so the query could run at all —
/// the draft starts its filters at these and says so.
/// </param>
internal sealed record QueryProfile(
    DynamicQuery Query, IReadOnlyList<QueryParameter> Parameters, int Rows, IReadOnlyList<ColumnProfile> Columns,
    IReadOnlyDictionary<string, string> AssumedValues)
{
    public IEnumerable<string> NumberColumns => Columns.Where(c => c.Kind == ColumnProfile.Number).Select(c => c.Name);

    /// <summary>The column of that name, case-insensitively — Oracle upper-cases unquoted aliases.</summary>
    public ColumnProfile? Column(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Columns.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Learns what a query returns by running it — the only way, as with the builder's column probe
/// — through <c>ExecuteQueryCommand</c>, so the caller's access check, the row cap and the
/// execution log all apply. Parameters take their defaults, date presets resolved, which is what
/// the draft's filters will send.
///
/// <para>Only the shape leaves this class. The rows are read to tell a number column from a text
/// one and to count distinct values, then dropped: sending values to the model is a separate
/// decision (docs/ai.html#decisions, sample rows off by default).</para>
/// </summary>
internal static class QueryProfiler
{
    /// <summary>Enough rows to tell a column's kind and roughly how many categories it has.</summary>
    public const int SampleRows = 200;

    public static async Task<QueryProfile> ProfileAsync(
        Guid queryId, IUnitOfWork unitOfWork, IMediator mediator, IAppLocalizer messages, CancellationToken cancellationToken)
    {
        var query = await unitOfWork.DynamicQueries.GetByIdAsync(queryId, cancellationToken)
            ?? throw new NotFoundException(nameof(DynamicQuery), queryId);

        if (query.QueryType.IsWrite())
            throw new DomainException(messages[MessageKeys.AiDraftWriteQuery]);

        var parameters = (await unitOfWork.QueryParameters.FindAsync(
                p => p.DynamicQueryId == queryId, cancellationToken))
            .OrderBy(p => p.SortOrder)
            .ToList();

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assumed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dateNumber = 0;
        foreach (var p in parameters)
        {
            var isDate = p.ParameterType == ParameterType.Date;
            var position = isDate ? dateNumber++ : 0;
            var value = p.DefaultValue;
            if (string.IsNullOrWhiteSpace(value) && p.IsRequired)
            {
                value = Assume(p, position);
                if (value is not null)
                    assumed[p.Name] = value;
            }
            if (!string.IsNullOrWhiteSpace(value))
                values[p.Name] = isDate ? DatePresets.Resolve(value, DateTime.Today) : value;
        }

        var result = await mediator.Send(new ExecuteQueryCommand { QueryId = queryId, Parameters = values }, cancellationToken);
        var rows = result.Rows.Take(SampleRows).ToList();

        var columns = result.Columns.Select(name =>
        {
            var cells = rows.Select(r => r.TryGetValue(name, out var v) ? v : null).ToList();
            var present = cells.Where(v => v is not null && v is not DBNull).ToList();
            return new ColumnProfile(
                name,
                KindOf(present),
                present.Select(v => v!.ToString()).Distinct().Count(),
                present.Count < cells.Count);
        }).ToList();

        return new QueryProfile(
            query, parameters, result.TotalRows > 0 ? result.TotalRows : result.Rows.Count, columns, assumed);
    }

    /// <summary>
    /// A starting value for a required parameter the query gives no default — without one the
    /// query will not run, and many report queries ask for a period. Dates follow the usual
    /// from/to pair: the first a year back, any later one today. A fixed-list dropdown takes its
    /// first option. Anything else cannot be guessed, and the query is left out with the reason.
    /// </summary>
    private static string? Assume(QueryParameter parameter, int datePosition)
    {
        if (parameter.ParameterType == ParameterType.Date)
            return datePosition == 0 ? "today-365" : "today";

        if (parameter.ParameterType == ParameterType.Dropdown
            && parameter.DropdownSourceType == DropdownSourceType.Static
            && !string.IsNullOrWhiteSpace(parameter.DropdownStaticValues))
        {
            try
            {
                using var options = System.Text.Json.JsonDocument.Parse(parameter.DropdownStaticValues);
                var first = options.RootElement.EnumerateArray()
                    .Select(o => o.TryGetProperty("value", out var v) ? v.ToString() : null)
                    .FirstOrDefault(v => !string.IsNullOrEmpty(v));
                if (first is null)
                    return null;
                // A multi-select travels as a JSON array, as from the run page.
                return parameter.AllowMultiple ? System.Text.Json.JsonSerializer.Serialize(new[] { first }) : first;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                // Not a JSON array of options: nothing to pick from.
                return null;
            }
        }

        return null;
    }

    /// <summary>The kind every non-null value agrees on; text when they disagree or there are none.</summary>
    private static string KindOf(IReadOnlyCollection<object?> values)
    {
        if (values.Count == 0) return ColumnProfile.Text;
        if (values.All(IsNumber)) return ColumnProfile.Number;
        if (values.All(v => v is DateTime or DateTimeOffset or DateOnly)) return ColumnProfile.Date;
        if (values.All(v => v is bool)) return ColumnProfile.Boolean;
        return ColumnProfile.Text;
    }

    private static bool IsNumber(object? value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}
