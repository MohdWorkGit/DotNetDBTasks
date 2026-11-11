using System.Text;
using System.Text.RegularExpressions;
using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Common.Reporting;
using Jadwal.Application.Features.Reports.Dtos;
using Jadwal.Domain.Entities;
using Jadwal.Domain.Enums;

namespace Jadwal.Application.Features.Ai;

/// <summary>The model's answer, in the shape <see cref="AiPrompts.ReportSchema"/> holds it to.</summary>
internal sealed class AiReportAnswer
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<AiReportSectionAnswer> Sections { get; set; } = new();
    public List<AiReportChartAnswer> Charts { get; set; } = new();
}

internal sealed class AiReportSectionAnswer
{
    /// <summary>1-based, as the queries are numbered in the prompt.</summary>
    public int Query { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

internal sealed class AiReportChartAnswer
{
    public int Query { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string CategoryColumn { get; set; } = string.Empty;
    public List<string> SeriesColumns { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Turns the model's proposal into a report the builder can load and the save will accept.
///
/// <para>Every picked query becomes a section, whatever the model said; it only titles and names
/// them. Charts are checked against the real columns and repaired or left out with a note — and
/// only offered on a query that is already a summary, because a report chart draws one row per
/// category and does not add rows up. The parameters and their mappings are built here from the
/// queries' own parameters, never by the model, in the form the report builder can hold: text in
/// place of a dropdown, one value, and fixed dates, since a report does not move a date by
/// itself.</para>
/// </summary>
internal sealed partial class ReportDraftBuilder
{
    public const int MaxCharts = 8;

    /// <summary>More rows than this is a list, not a summary: one bar per row would be unreadable.</summary>
    public const int MaxChartRows = 50;

    private const int KeyMaxLength = 40;
    private const int TitleMaxLength = 200;

    public static readonly IReadOnlyDictionary<string, ReportChartType> ChartTypes =
        new Dictionary<string, ReportChartType>(StringComparer.OrdinalIgnoreCase)
        {
            ["column"] = ReportChartType.Column,
            ["bar"] = ReportChartType.Bar,
            ["line"] = ReportChartType.Line,
            ["area"] = ReportChartType.Area,
            ["pie"] = ReportChartType.Pie,
            ["doughnut"] = ReportChartType.Doughnut,
            ["stackedColumn"] = ReportChartType.StackedColumn,
            ["stackedBar"] = ReportChartType.StackedBar
        };

    private readonly IReadOnlyList<QueryProfile> _profiles;
    private readonly IAppLocalizer _messages;
    private readonly DateTime _today;

    public ReportDraftBuilder(IReadOnlyList<QueryProfile> profiles, IAppLocalizer messages, DateTime today)
    {
        _profiles = profiles;
        _messages = messages;
        _today = today;
    }

    public List<string> Notes { get; } = new();

    /// <summary>The draft, and the model's reason for each chart kept, in chart order.</summary>
    public (ReportInput Draft, List<string> ChartReasons) Build(AiReportAnswer answer)
    {
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parameters = BuildParameters(out var parameterNames);

        var datasets = new List<ReportDatasetInput>();
        for (var i = 0; i < _profiles.Count; i++)
        {
            var profile = _profiles[i];
            var section = answer.Sections.FirstOrDefault(s => s.Query == i + 1);
            datasets.Add(new ReportDatasetInput
            {
                DatasetKey = UniqueKey(section?.Key, $"section{i + 1}", usedKeys),
                DisplayName = Clip(section?.Title) is { Length: > 0 } title ? title : Clip(profile.Query.Name),
                SourceType = ReportDatasetSourceType.Query,
                SortOrder = i,
                IsVisibleInViewer = true,
                DynamicQueryId = profile.Query.Id,
                // Every parameter is mapped: an unmapped required one would fail the section.
                ParameterMaps = profile.Parameters.Select(p => new ReportParameterMapInput
                {
                    TargetParameterName = p.Name,
                    SourceKind = ReportParameterSourceKind.ReportParameter,
                    ReportParameterName = parameterNames[p.Name]
                }).ToList()
            });
        }

        var charts = new List<ReportChartInput>();
        var reasons = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proposed in answer.Charts)
        {
            var title = Clip(proposed.Title);
            var (chart, problem) = Repair(proposed, datasets);
            if (problem is not null)
            {
                Notes.Add(_messages[MessageKeys.AiDraftChartDropped, title.Length > 0 ? title : "?", problem]);
                continue;
            }

            var key = $"{chart!.DatasetKey}|{chart.ChartType}|{chart.CategoryColumn}|{string.Join(",", chart.SeriesColumns)}";
            if (!seen.Add(key))
            {
                Notes.Add(_messages[MessageKeys.AiDraftChartDropped, chart.Title ?? "?", _messages[MessageKeys.AiDraftDuplicate]]);
                continue;
            }
            if (charts.Count == MaxCharts)
            {
                Notes.Add(_messages[MessageKeys.AiDraftTooManyCharts, MaxCharts]);
                break;
            }

            chart.SortOrder = charts.Count;
            chart.ChartKey = UniqueKey(null, $"chart{charts.Count + 1}", usedKeys);
            charts.Add(chart);
            reasons.Add(proposed.Reason.Trim());
        }

        var draft = new ReportInput
        {
            Name = Clip(answer.Name) is { Length: > 0 } name ? name : Clip(_profiles[0].Query.Name),
            Description = string.IsNullOrWhiteSpace(answer.Description) ? null : answer.Description.Trim(),
            Datasets = datasets,
            Parameters = parameters,
            Charts = charts
        };
        return (draft, reasons);
    }

    private (ReportChartInput? Chart, string? Problem) Repair(AiReportChartAnswer proposed, List<ReportDatasetInput> datasets)
    {
        if (proposed.Query < 1 || proposed.Query > _profiles.Count)
            return (null, _messages[MessageKeys.AiDraftUnknownQuery]);
        var profile = _profiles[proposed.Query - 1];

        if (profile.Rows > MaxChartRows)
            return (null, _messages[MessageKeys.AiDraftChartNeedsSummary, profile.Rows]);

        if (!ChartTypes.TryGetValue(proposed.Type.Trim(), out var type))
            type = ReportChartType.Column;

        var category = profile.Column(proposed.CategoryColumn);
        if (category is null || category.Kind == ColumnProfile.Number)
        {
            var others = profile.Columns.Where(c => c.Kind != ColumnProfile.Number).ToList();
            if (others.Count != 1)
                return (null, _messages[MessageKeys.AiDraftNoCategory]);
            category = others[0];
        }

        var series = proposed.SeriesColumns
            .Select(profile.Column)
            .Where(c => c is not null && c.Kind == ColumnProfile.Number && c.Name != category.Name)
            .Select(c => c!.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (series.Count == 0)
            series = profile.NumberColumns.Where(n => n != category.Name).Take(3).ToList();
        if (series.Count == 0)
            return (null, _messages[MessageKeys.AiDraftNoNumbers]);

        if (type is ReportChartType.Pie or ReportChartType.Doughnut)
            series = series.Take(1).ToList();
        if (series.Count < 2 && type is ReportChartType.StackedColumn or ReportChartType.StackedBar)
            type = type == ReportChartType.StackedBar ? ReportChartType.Bar : ReportChartType.Column;

        return (new ReportChartInput
        {
            Title = Clip(proposed.Title) is { Length: > 0 } title ? title : Clip(profile.Query.Name),
            ChartType = type,
            DatasetKey = datasets[proposed.Query - 1].DatasetKey,
            CategoryColumn = category.Name,
            SeriesColumns = series,
            // The usual 25, or every row of a longer summary (at most MaxChartRows).
            MaxCategories = Math.Max(25, profile.Rows)
        }, null);
    }

    /// <summary>
    /// One report parameter per parameter name across the queries, in the form the report builder
    /// can hold, each change from the query's own definition said in a note.
    /// </summary>
    private List<ReportParameterInput> BuildParameters(out Dictionary<string, string> names)
    {
        names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parameters = new List<ReportParameterInput>();
        foreach (var (parameter, profile) in _profiles.SelectMany(p => p.Parameters.Select(parameter => (parameter, p))))
        {
            if (names.ContainsKey(parameter.Name))
                continue;

            var name = ParameterNameRegex().IsMatch(parameter.Name) ? parameter.Name : "p_" + parameter.Name;
            names[parameter.Name] = name;
            var label = string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Name : parameter.DisplayName;

            var type = parameter.ParameterType;
            if (type == ParameterType.Dropdown)
            {
                type = ParameterType.String;
                Notes.Add(_messages[MessageKeys.AiDraftParameterAsText, label]);
            }
            if (parameter.AllowMultiple)
                Notes.Add(_messages[MessageKeys.AiDraftParameterSingle, label]);

            var defaultValue = parameter.DefaultValue;
            if (string.IsNullOrWhiteSpace(defaultValue) && profile.AssumedValues.TryGetValue(parameter.Name, out var assumed))
                defaultValue = assumed;
            if (type == ParameterType.Date && !string.IsNullOrWhiteSpace(defaultValue))
            {
                // A report keeps a date as it is written: a shortcut such as today-30 would be
                // refused when the report runs, so it becomes the date it means today.
                var fixedDate = DatePresets.Resolve(defaultValue, _today);
                if (!DateTime.TryParse(fixedDate, out _))
                    fixedDate = null;
                else if (fixedDate != defaultValue || profile.AssumedValues.ContainsKey(parameter.Name))
                    Notes.Add(_messages[MessageKeys.AiDraftFixedDate, label, fixedDate]);
                defaultValue = fixedDate;
            }

            parameters.Add(new ReportParameterInput
            {
                Name = name,
                DisplayName = label,
                ParameterType = type,
                IsRequired = parameter.IsRequired,
                DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue,
                SortOrder = parameters.Count
            });
        }
        return parameters;
    }

    /// <summary>
    /// A dataset or chart key the save accepts — a letter, then letters, digits or underscores —
    /// from the model's English suggestion, or the fallback, made unique across both.
    /// </summary>
    private static string UniqueKey(string? proposed, string fallback, HashSet<string> used)
    {
        var cleaned = new StringBuilder();
        foreach (var c in proposed ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '_')
                cleaned.Append(c);
            else if (c is ' ' or '-')
                cleaned.Append('_');
        }
        var key = cleaned.ToString().TrimStart('_', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Trim('_');
        if (key.Length > KeyMaxLength) key = key[..KeyMaxLength].TrimEnd('_');
        if (key.Length == 0) key = fallback;

        var unique = key;
        for (var n = 2; !used.Add(unique); n++)
            unique = $"{key}_{n}";
        return unique;
    }

    private static string Clip(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        return trimmed.Length <= TitleMaxLength ? trimmed : trimmed[..TitleMaxLength];
    }

    /// <summary>The report builder's rule for a parameter name.</summary>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,99}$")]
    private static partial Regex ParameterNameRegex();
}
