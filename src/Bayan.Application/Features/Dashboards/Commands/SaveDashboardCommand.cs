using System.Text.Json;
using System.Text.RegularExpressions;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Reporting;
using Bayan.Application.Features.Dashboards.Dtos;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Dashboards.Commands;

/// <summary>
/// Creates a dashboard, or replaces an existing one's whole definition. Tiles, filters and maps
/// are saved as one graph rather than diffed, for the reason <c>SaveReportCommand</c> gives: a
/// tile's maps name filters, and a partial update is how one ends up naming a filter that is gone.
/// </summary>
public class SaveDashboardCommand : IRequest<DashboardDto>
{
    /// <summary>Null creates; set updates that dashboard.</summary>
    public Guid? Id { get; set; }

    public DashboardInput Input { get; set; } = new();
}

public partial class SaveDashboardCommandHandler : IRequestHandler<SaveDashboardCommand, DashboardDto>
{
    private static readonly int[] AllowedWidths = { 3, 4, 6, 8, 12 };

    /// <summary>More than this is a spreadsheet, not a highlight.</summary>
    private const int MaxRules = 20;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IDashboardTileCache _cache;

    public SaveDashboardCommandHandler(
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, IDashboardTileCache cache)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _cache = cache;
    }

    public async Task<DashboardDto> Handle(SaveDashboardCommand request, CancellationToken cancellationToken)
    {
        var input = request.Input;
        Validate(input);
        await EnsureQueriesAreReadsAsync(input, cancellationToken);

        Dashboard dashboard;
        if (request.Id is null)
        {
            dashboard = new Dashboard
            {
                Id = Guid.NewGuid(),
                CreatedByUserId = _currentUser.UserId,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Dashboards.AddAsync(dashboard, cancellationToken);
        }
        else
        {
            dashboard = await _unitOfWork.Dashboards.GetByIdAsync(
                request.Id.Value, cancellationToken, "Tiles", "Tiles.ParameterMaps", "Filters")
                ?? throw new NotFoundException(nameof(Dashboard), request.Id.Value);

            // Replace the graph. Maps cascade from their tile.
            foreach (var tile in dashboard.Tiles.ToList())
                _unitOfWork.DashboardTiles.Delete(tile);
            foreach (var filter in dashboard.Filters.ToList())
                _unitOfWork.DashboardFilters.Delete(filter);

            dashboard.UpdatedAt = DateTime.UtcNow;
        }

        dashboard.Name = input.Name.Trim();
        // Nullable rather than empty: Oracle stores '' as NULL.
        dashboard.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        dashboard.IsEnabled = input.IsEnabled;
        dashboard.DefaultRefreshSeconds = input.DefaultRefreshSeconds;
        dashboard.SortOrder = input.SortOrder;

        // Filter ids are assigned up front so a tile can reference one created in the same save.
        var filterIdsByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var filterInput in input.Filters)
        {
            var filter = BuildFilter(dashboard.Id, filterInput);
            filterIdsByName[filter.Name] = filter.Id;
            await _unitOfWork.DashboardFilters.AddAsync(filter, cancellationToken);
        }

        foreach (var tileInput in input.Tiles)
        {
            var tile = BuildTile(dashboard.Id, tileInput, filterIdsByName);
            await _unitOfWork.DashboardTiles.AddAsync(tile, cancellationToken);

            foreach (var mapInput in tileInput.ParameterMaps)
            {
                Guid? filterId = null;
                if (mapInput.SourceKind == DashboardTileParameterSource.Filter)
                    filterId = filterIdsByName[mapInput.FilterName!];   // Validate() guarantees it

                await _unitOfWork.DashboardTileParameterMaps.AddAsync(new DashboardTileParameterMap
                {
                    Id = Guid.NewGuid(),
                    DashboardTileId = tile.Id,
                    TargetParameterName = mapInput.TargetParameterName.Trim(),
                    SourceKind = mapInput.SourceKind,
                    DashboardFilterId = filterId,
                    ConstantValue = mapInput.SourceKind == DashboardTileParameterSource.Constant &&
                                    !string.IsNullOrWhiteSpace(mapInput.ConstantValue)
                        ? mapInput.ConstantValue
                        : null,
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Tile ids change on every save, so the old entries could never be hit again anyway;
        // dropping them now just returns the memory straight away.
        _cache.InvalidateDashboard(dashboard.Id);

        return await DashboardMapper.LoadDtoAsync(_unitOfWork, dashboard.Id, cancellationToken);
    }

    private static void Validate(DashboardInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new DomainException("The dashboard needs a name.");

        CheckRefresh(input.DefaultRefreshSeconds, "The dashboard's refresh interval");

        var filterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filter in input.Filters)
        {
            // The name travels in the dashboard URL and in the tile maps, so it is held to the
            // same shape as a query parameter name.
            if (!NameRegex().IsMatch(filter.Name ?? string.Empty))
            {
                throw new DomainException(
                    $"Filter name '{filter.Name}' is not valid. Use letters, numbers and underscores only.");
            }

            if (!filterNames.Add(filter.Name!))
                throw new DomainException($"Filter '{filter.Name}' is declared more than once.");

            if (filter.ParameterType == ParameterType.Date &&
                !string.IsNullOrWhiteSpace(filter.DefaultValue) &&
                !DatePresets.IsValidDateValue(filter.DefaultValue))
            {
                throw new DomainException(
                    $"Filter '{filter.Name}' has default '{filter.DefaultValue}', which is neither a date nor one of " +
                    $"{string.Join(", ", DatePresets.Tokens)}, today-N or today+N.");
            }

            if (filter.ParameterType == ParameterType.Dropdown && filter.DropdownSourceType is null)
                throw new DomainException($"Filter '{filter.Name}' is a dropdown but has no source for its options.");

            if (filter.DropdownSourceType == DropdownSourceType.Query &&
                (filter.DropdownQueryId is null ||
                 string.IsNullOrWhiteSpace(filter.DropdownQueryValueColumn)))
            {
                throw new DomainException($"Filter '{filter.Name}' needs a lookup query and a value column.");
            }
        }

        foreach (var tile in input.Tiles)
        {
            var label = string.IsNullOrWhiteSpace(tile.Title) ? "A tile" : $"Tile '{tile.Title}'";

            if (string.IsNullOrWhiteSpace(tile.Title))
                throw new DomainException("Every tile needs a title.");

            if (tile.DynamicQueryId == Guid.Empty)
                throw new DomainException($"{label} has no query assigned.");

            if (!AllowedWidths.Contains(tile.Width))
                throw new DomainException($"{label} has width {tile.Width}; use 3, 4, 6, 8 or 12 columns.");

            if (tile.Height is < 1 or > 3)
                throw new DomainException($"{label} has height {tile.Height}; use 1 to 3 rows.");

            if (tile.RefreshSeconds is { } seconds)
                CheckRefresh(seconds, $"{label}'s refresh interval");

            if (!Enum.IsDefined(tile.VisualType))
                throw new DomainException($"{label} has an unknown visual type.");

            switch (tile.VisualType)
            {
                case DashboardVisualType.Kpi:
                    if (!Enum.IsDefined(tile.KpiAggregate))
                        throw new DomainException($"{label} has an unknown summary mode.");
                    // Counting rows reads no column; every other mode needs one.
                    if (tile.KpiAggregate != DashboardKpiAggregate.Count && string.IsNullOrWhiteSpace(tile.ValueColumn))
                        throw new DomainException($"{label} is a KPI, so it needs a value column.");
                    if (tile.TargetWarnPercent is < 0 or > 100)
                        throw new DomainException($"{label}: the 'near target' margin must be between 0 and 100 percent.");
                    break;

                case DashboardVisualType.Table:
                    break;

                default:
                    if (string.IsNullOrWhiteSpace(tile.CategoryColumn))
                        throw new DomainException($"{label} is a chart, so it needs a category column.");
                    if (tile.SeriesColumns.Count == 0)
                        throw new DomainException($"{label} is a chart, so it needs at least one series column.");
                    break;
            }

            if (tile.ConditionalRules.Count > MaxRules)
                throw new DomainException($"{label} has more than {MaxRules} colour rules.");

            foreach (var rule in tile.ConditionalRules)
            {
                if (!DashboardConditionalRule.Operators.Contains(rule.Operator ?? string.Empty))
                    throw new DomainException($"{label} has a colour rule with an unknown comparison '{rule.Operator}'.");
                if (!DashboardConditionalRule.Tones.Contains(rule.Tone ?? string.Empty))
                    throw new DomainException($"{label} has a colour rule with an unknown colour '{rule.Tone}'.");
                if (string.IsNullOrWhiteSpace(rule.Value))
                    throw new DomainException($"{label} has a colour rule with nothing to compare against.");
                if (tile.VisualType == DashboardVisualType.Table && string.IsNullOrWhiteSpace(rule.Column))
                    throw new DomainException($"{label} has a colour rule that names no column.");
            }

            switch (tile.DrillAction)
            {
                case DashboardDrillAction.FilterDashboard:
                    if (tile.DrillFilterName is null || !filterNames.Contains(tile.DrillFilterName))
                        throw new DomainException($"{label} drills into a filter this dashboard does not have.");
                    break;

                case DashboardDrillAction.OpenReport:
                    if (tile.DrillReportId is null)
                        throw new DomainException($"{label} opens a report on click, but no report is chosen.");
                    break;
            }

            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in tile.ParameterMaps)
            {
                if (string.IsNullOrWhiteSpace(map.TargetParameterName))
                    throw new DomainException($"{label} has a parameter mapping with no target parameter.");

                if (!targets.Add(map.TargetParameterName))
                    throw new DomainException($"{label} maps '{map.TargetParameterName}' more than once.");

                if (map.SourceKind == DashboardTileParameterSource.Filter &&
                    (map.FilterName is null || !filterNames.Contains(map.FilterName)))
                {
                    throw new DomainException(
                        $"{label} maps '{map.TargetParameterName}' to filter '{map.FilterName}', " +
                        "which this dashboard does not declare.");
                }
            }
        }
    }

    private static void CheckRefresh(int seconds, string what)
    {
        if (seconds < DashboardMapper.MinRefreshSeconds || seconds > DashboardMapper.MaxRefreshSeconds)
        {
            throw new DomainException(
                $"{what} must be between {DashboardMapper.MinRefreshSeconds} and " +
                $"{DashboardMapper.MaxRefreshSeconds} seconds.");
        }
    }

    /// <summary>
    /// Every tile's query must exist and must return rows. A write query on a timer would run the
    /// write every refresh — once per interval for as long as anyone had the page open.
    /// </summary>
    private async Task EnsureQueriesAreReadsAsync(DashboardInput input, CancellationToken cancellationToken)
    {
        foreach (var queryId in input.Tiles.Select(t => t.DynamicQueryId).Distinct())
        {
            var query = await _unitOfWork.DynamicQueries.GetByIdAsync(queryId, cancellationToken)
                ?? throw new NotFoundException(nameof(DynamicQuery), queryId);

            if (query.QueryType.IsWrite())
                throw new DomainException($"Query '{query.Name}' changes data, so it cannot feed a dashboard tile.");
        }
    }

    private static DashboardFilter BuildFilter(Guid dashboardId, DashboardFilterInput input) => new()
    {
        Id = Guid.NewGuid(),
        DashboardId = dashboardId,
        Name = input.Name.Trim(),
        DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? input.Name.Trim() : input.DisplayName.Trim(),
        ParameterType = input.ParameterType,
        IsRequired = input.IsRequired,
        DefaultValue = string.IsNullOrWhiteSpace(input.DefaultValue) ? null : input.DefaultValue,
        SortOrder = input.SortOrder,
        AllowMultiple = input.AllowMultiple,
        DropdownSourceType = input.ParameterType == ParameterType.Dropdown ? input.DropdownSourceType : null,
        DropdownStaticValues = string.IsNullOrWhiteSpace(input.DropdownStaticValues) ? null : input.DropdownStaticValues,
        DropdownQueryId = input.DropdownQueryId,
        DropdownQueryValueColumn = string.IsNullOrWhiteSpace(input.DropdownQueryValueColumn) ? null : input.DropdownQueryValueColumn.Trim(),
        DropdownQueryLabelColumn = string.IsNullOrWhiteSpace(input.DropdownQueryLabelColumn) ? null : input.DropdownQueryLabelColumn.Trim(),
        CreatedAt = DateTime.UtcNow
    };

    private static DashboardTile BuildTile(
        Guid dashboardId,
        DashboardTileInput input,
        IReadOnlyDictionary<string, Guid> filterIdsByName) => new()
    {
        Id = Guid.NewGuid(),
        DashboardId = dashboardId,
        Title = input.Title.Trim(),
        DynamicQueryId = input.DynamicQueryId,
        SortOrder = input.SortOrder,
        Width = input.Width,
        Height = input.Height,
        VisualType = input.VisualType,
        CategoryColumn = Blank(input.CategoryColumn),
        SeriesColumnsJson = JsonSerializer.Serialize(
            input.SeriesColumns.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())),
        MaxCategories = input.MaxCategories <= 0 ? 25 : input.MaxCategories,
        ValueColumn = Blank(input.ValueColumn),
        CompareColumn = Blank(input.CompareColumn),
        KpiAggregate = input.VisualType == DashboardVisualType.Kpi ? input.KpiAggregate : DashboardKpiAggregate.Last,
        ValueFormat = input.ValueFormat,
        TargetValue = input.VisualType == DashboardVisualType.Kpi ? input.TargetValue : null,
        TargetWarnPercent = input.TargetWarnPercent,
        // Only the tiles that draw them keep rules; a chart has no cell to colour.
        ConditionalRulesJson = input.ConditionalRules.Count > 0 &&
                               input.VisualType is DashboardVisualType.Table or DashboardVisualType.Kpi
            ? JsonSerializer.Serialize(input.ConditionalRules.Select(r => new DashboardConditionalRule
                {
                    Column = Blank(r.Column),
                    Operator = r.Operator,
                    Value = r.Value.Trim(),
                    Tone = r.Tone
                }), DashboardMapper.JsonOptions)
            : null,
        HigherIsBetter = input.HigherIsBetter,
        RefreshSeconds = input.RefreshSeconds,
        DrillAction = input.DrillAction,
        DrillFilterId = input.DrillAction == DashboardDrillAction.FilterDashboard
            ? filterIdsByName[input.DrillFilterName!]
            : null,
        DrillReportId = input.DrillAction == DashboardDrillAction.OpenReport ? input.DrillReportId : null,
        DrillReportParameter = input.DrillAction == DashboardDrillAction.OpenReport
            ? Blank(input.DrillReportParameter)
            : null,
        CreatedAt = DateTime.UtcNow
    };

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,99}$")]
    private static partial Regex NameRegex();
}
