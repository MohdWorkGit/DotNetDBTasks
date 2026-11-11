using System.Text.RegularExpressions;
using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Common.Reporting;
using Jadwal.Application.Features.Dashboards.Dtos;
using Jadwal.Domain.Entities;
using Jadwal.Domain.Enums;

namespace Jadwal.Application.Features.Ai;

/// <summary>The model's answer, in the shape <see cref="AiPrompts.DashboardSchema"/> holds it to.</summary>
internal sealed class AiDashboardAnswer
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<AiTileAnswer> Tiles { get; set; } = new();
}

internal sealed class AiTileAnswer
{
    /// <summary>1-based, as the queries are numbered in the prompt.</summary>
    public int Query { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Visual { get; set; } = string.Empty;
    public string CategoryColumn { get; set; } = string.Empty;
    public List<string> SeriesColumns { get; set; } = new();
    public string ValueColumn { get; set; } = string.Empty;
    public string Aggregate { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public int Width { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Turns the model's proposal into a dashboard the builder can load and the save will accept.
///
/// <para>The model only suggests. Every tile is checked against what its query really returns
/// and repaired where the fix is obvious — a series that is not a number is dropped, a chart with
/// no valid series takes the query's number columns, a stacked chart with one series becomes a
/// column chart — or left out with a note saying why. Sizes are set here rather than trusted, and
/// the filters and parameter mappings are built from the queries' own parameters, never by the
/// model: getting a required parameter wrong would break the tile.</para>
/// </summary>
internal sealed partial class DashboardDraftBuilder
{
    public const int MaxTiles = 8;
    private const int TitleMaxLength = 200;

    /// <summary>The visual types offered to the model, by the names its schema uses.</summary>
    public static readonly IReadOnlyDictionary<string, DashboardVisualType> Visuals =
        new Dictionary<string, DashboardVisualType>(StringComparer.OrdinalIgnoreCase)
        {
            ["kpi"] = DashboardVisualType.Kpi,
            ["column"] = DashboardVisualType.Column,
            ["bar"] = DashboardVisualType.Bar,
            ["line"] = DashboardVisualType.Line,
            ["area"] = DashboardVisualType.Area,
            ["pie"] = DashboardVisualType.Pie,
            ["doughnut"] = DashboardVisualType.Doughnut,
            ["funnel"] = DashboardVisualType.Funnel,
            ["stackedColumn"] = DashboardVisualType.StackedColumn,
            ["stackedBar"] = DashboardVisualType.StackedBar,
            ["table"] = DashboardVisualType.Table
        };

    public static readonly IReadOnlyDictionary<string, DashboardKpiAggregate> Aggregates =
        new Dictionary<string, DashboardKpiAggregate>(StringComparer.OrdinalIgnoreCase)
        {
            ["last"] = DashboardKpiAggregate.Last,
            ["sum"] = DashboardKpiAggregate.Sum,
            ["average"] = DashboardKpiAggregate.Average,
            ["count"] = DashboardKpiAggregate.Count,
            ["min"] = DashboardKpiAggregate.Min,
            ["max"] = DashboardKpiAggregate.Max
        };

    public static readonly IReadOnlyDictionary<string, DashboardValueFormat> Formats =
        new Dictionary<string, DashboardValueFormat>(StringComparer.OrdinalIgnoreCase)
        {
            ["number"] = DashboardValueFormat.Number,
            ["percent"] = DashboardValueFormat.Percent,
            ["currency"] = DashboardValueFormat.Currency
        };

    public static readonly int[] Widths = { 3, 4, 6, 8, 12 };

    private static readonly DashboardVisualType[] SingleSeries =
        { DashboardVisualType.Pie, DashboardVisualType.Doughnut, DashboardVisualType.Funnel };

    private readonly IReadOnlyList<QueryProfile> _profiles;
    private readonly IAppLocalizer _messages;

    public DashboardDraftBuilder(IReadOnlyList<QueryProfile> profiles, IAppLocalizer messages)
    {
        _profiles = profiles;
        _messages = messages;
    }

    public List<string> Notes { get; } = new();

    /// <summary>The draft, and the model's reason for each tile kept, in tile order.</summary>
    public (DashboardInput Draft, List<string> Reasons) Build(AiDashboardAnswer answer)
    {
        var kept = new List<(DashboardTileInput Tile, QueryProfile Profile, string Reason)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var proposed in answer.Tiles)
        {
            var title = Clip(proposed.Title);
            var (tile, profile, problem) = Repair(proposed);
            if (problem is not null)
            {
                Notes.Add(_messages[MessageKeys.AiDraftTileDropped, title.Length > 0 ? title : "?", problem]);
                continue;
            }

            var key = $"{tile!.DynamicQueryId}|{tile.VisualType}|{tile.CategoryColumn}|{string.Join(",", tile.SeriesColumns)}|{tile.ValueColumn}|{tile.KpiAggregate}";
            if (!seen.Add(key))
            {
                Notes.Add(_messages[MessageKeys.AiDraftTileDropped, tile.Title, _messages[MessageKeys.AiDraftDuplicate]]);
                continue;
            }

            kept.Add((tile, profile!, proposed.Reason.Trim()));
        }

        if (kept.Count > MaxTiles)
        {
            Notes.Add(_messages[MessageKeys.AiDraftTooManyTiles, MaxTiles]);
            kept = kept.Take(MaxTiles).ToList();
        }

        // Figures first, then charts, then tables — the order the prompt asks for, held here too.
        kept = kept.OrderBy(k => Rank(k.Tile.VisualType)).ToList();

        var filters = BuildFilters(kept.Select(k => k.Profile).Distinct().ToList(), out var filterNames);
        for (var i = 0; i < kept.Count; i++)
        {
            var (tile, profile, _) = kept[i];
            tile.SortOrder = i;
            tile.ParameterMaps = profile.Parameters.Select(p => new DashboardTileParameterMapDto
            {
                TargetParameterName = p.Name,
                SourceKind = DashboardTileParameterSource.Filter,
                FilterName = filterNames[p.Name]
            }).ToList();
        }

        var firstQuery = kept.Count > 0 ? kept[0].Profile.Query.Name : string.Empty;
        var draft = new DashboardInput
        {
            Name = Clip(answer.Name) is { Length: > 0 } name ? name : firstQuery,
            Description = string.IsNullOrWhiteSpace(answer.Description) ? null : answer.Description.Trim(),
            DefaultRefreshSeconds = 60,
            Tiles = kept.Select(k => k.Tile).ToList(),
            Filters = filters
        };
        return (draft, kept.Select(k => k.Reason).ToList());
    }

    private (DashboardTileInput? Tile, QueryProfile? Profile, string? Problem) Repair(AiTileAnswer proposed)
    {
        if (proposed.Query < 1 || proposed.Query > _profiles.Count)
            return (null, null, _messages[MessageKeys.AiDraftUnknownQuery]);
        var profile = _profiles[proposed.Query - 1];

        if (!Visuals.TryGetValue(proposed.Visual.Trim(), out var visual))
            visual = DashboardVisualType.Table;

        var tile = new DashboardTileInput
        {
            Title = Clip(proposed.Title) is { Length: > 0 } title ? title : Clip(profile.Query.Name),
            DynamicQueryId = profile.Query.Id,
            VisualType = visual,
            ValueFormat = Formats.TryGetValue(proposed.Format.Trim(), out var format) ? format : DashboardValueFormat.Number
        };

        var numbers = profile.NumberColumns.ToList();

        if (visual == DashboardVisualType.Kpi)
        {
            tile.KpiAggregate = Aggregates.TryGetValue(proposed.Aggregate.Trim(), out var aggregate)
                ? aggregate : DashboardKpiAggregate.Last;
            var value = profile.Column(proposed.ValueColumn);
            if (tile.KpiAggregate != DashboardKpiAggregate.Count && value?.Kind != ColumnProfile.Number)
            {
                // A figure needs a number; with only one in the query, there is no doubt which.
                if (numbers.Count != 1)
                    return (null, null, _messages[MessageKeys.AiDraftNoValue]);
                value = profile.Column(numbers[0]);
            }
            tile.ValueColumn = value?.Name;
        }
        else if (visual != DashboardVisualType.Table)
        {
            var category = profile.Column(proposed.CategoryColumn);
            if (category is null || category.Kind == ColumnProfile.Number)
            {
                // Only one column that is not a number: that is the axis.
                var others = profile.Columns.Where(c => c.Kind != ColumnProfile.Number).ToList();
                if (others.Count != 1)
                    return (null, null, _messages[MessageKeys.AiDraftNoCategory]);
                category = others[0];
            }
            tile.CategoryColumn = category.Name;

            var series = proposed.SeriesColumns
                .Select(profile.Column)
                .Where(c => c is not null && c.Kind == ColumnProfile.Number && c.Name != category.Name)
                .Select(c => c!.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (series.Count == 0)
                series = numbers.Where(n => n != category.Name).Take(3).ToList();
            if (series.Count == 0)
                return (null, null, _messages[MessageKeys.AiDraftNoNumbers]);

            if (SingleSeries.Contains(visual))
                series = series.Take(1).ToList();
            if (series.Count < 2 && visual is DashboardVisualType.StackedColumn or DashboardVisualType.StackedBar)
                tile.VisualType = visual == DashboardVisualType.StackedBar ? DashboardVisualType.Bar : DashboardVisualType.Column;
            tile.SeriesColumns = series;
        }

        tile.Width = Widths.Contains(proposed.Width) ? proposed.Width : DefaultWidth(tile.VisualType);
        tile.Height = tile.VisualType == DashboardVisualType.Kpi ? 1 : 2;
        return (tile, profile, null);
    }

    /// <summary>
    /// One filter per parameter name across the queries the draft uses, copied from the query's
    /// own definition — so the tile sends what the query expects, and the default the query was
    /// written with becomes the filter's starting value.
    /// </summary>
    private List<DashboardFilterInput> BuildFilters(
        IReadOnlyList<QueryProfile> profiles, out Dictionary<string, string> filterNames)
    {
        filterNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var filters = new List<DashboardFilterInput>();
        foreach (var (parameter, profile) in profiles.SelectMany(p => p.Parameters.Select(parameter => (parameter, p))))
        {
            if (filterNames.ContainsKey(parameter.Name))
                continue;

            var name = FilterNameRegex().IsMatch(parameter.Name) ? parameter.Name : "p_" + parameter.Name;
            filterNames[parameter.Name] = name;

            var type = parameter.ParameterType;
            // A dropdown without a source cannot be saved as a filter; it still takes typed values.
            if (type == ParameterType.Dropdown && parameter.DropdownSourceType is null)
                type = ParameterType.String;
            var defaultValue = parameter.DefaultValue;
            if (string.IsNullOrWhiteSpace(defaultValue) && profile.AssumedValues.TryGetValue(parameter.Name, out var assumed))
            {
                // The value the query was profiled with; the tile would fail without one.
                defaultValue = assumed;
                Notes.Add(_messages[MessageKeys.AiDraftAssumedDefault,
                    string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Name : parameter.DisplayName, assumed]);
            }
            if (type == ParameterType.Date && defaultValue is not null && !DatePresets.IsValidDateValue(defaultValue))
                defaultValue = null;

            filters.Add(new DashboardFilterInput
            {
                Name = name,
                DisplayName = string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Name : parameter.DisplayName,
                ParameterType = type,
                IsRequired = parameter.IsRequired,
                DefaultValue = defaultValue,
                SortOrder = filters.Count,
                AllowMultiple = parameter.AllowMultiple,
                DropdownSourceType = type == ParameterType.Dropdown ? parameter.DropdownSourceType : null,
                DropdownStaticValues = type == ParameterType.Dropdown ? parameter.DropdownStaticValues : null,
                DropdownQueryId = type == ParameterType.Dropdown ? parameter.DropdownQueryId : null,
                DropdownQueryValueColumn = type == ParameterType.Dropdown ? parameter.DropdownQueryValueColumn : null,
                DropdownQueryLabelColumn = type == ParameterType.Dropdown ? parameter.DropdownQueryLabelColumn : null
            });
        }
        return filters;
    }

    private static int DefaultWidth(DashboardVisualType visual) => visual switch
    {
        DashboardVisualType.Kpi => 3,
        DashboardVisualType.Table => 12,
        _ => 6
    };

    private static int Rank(DashboardVisualType visual) => visual switch
    {
        DashboardVisualType.Kpi => 0,
        DashboardVisualType.Table => 2,
        _ => 1
    };

    private static string Clip(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        return trimmed.Length <= TitleMaxLength ? trimmed : trimmed[..TitleMaxLength];
    }

    /// <summary>The save's rule for a filter name (<c>SaveDashboardCommand</c>).</summary>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,99}$")]
    private static partial Regex FilterNameRegex();
}
