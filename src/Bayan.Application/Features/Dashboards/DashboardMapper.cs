using System.Text.Json;
using Bayan.Application.Features.Dashboards.Dtos;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using Bayan.Domain.Services;

namespace Bayan.Application.Features.Dashboards;

public static class DashboardMapper
{
    /// <summary>Everything <see cref="ToDto"/> reads, as separate include paths.</summary>
    public static readonly string[] FullIncludes =
        { "Tiles", "Tiles.DynamicQuery", "Tiles.ParameterMaps", "Filters" };

    /// <summary>The fastest a tile may refresh. Below this the "shared" result is barely shared.</summary>
    public const int MinRefreshSeconds = 30;

    public const int MaxRefreshSeconds = 3600;

    public static int EffectiveRefreshSeconds(Dashboard dashboard, DashboardTile tile) =>
        Math.Clamp(tile.RefreshSeconds ?? dashboard.DefaultRefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds);

    public static DashboardSummaryDto ToSummary(Dashboard dashboard) =>
        Fill(new DashboardSummaryDto(), dashboard);

    public static DashboardDto ToDto(Dashboard dashboard)
    {
        var dto = Fill(new DashboardDto(), dashboard);
        var filterNames = dashboard.Filters.ToDictionary(f => f.Id, f => f.Name);

        dto.Filters = dashboard.Filters
            .OrderBy(f => f.SortOrder)
            .Select(f => new DashboardFilterDto
            {
                Id = f.Id,
                Name = f.Name,
                DisplayName = f.DisplayName,
                ParameterType = f.ParameterType,
                IsRequired = f.IsRequired,
                DefaultValue = f.DefaultValue,
                SortOrder = f.SortOrder,
                AllowMultiple = f.AllowMultiple,
                DropdownSourceType = f.DropdownSourceType,
                DropdownStaticValues = f.DropdownStaticValues,
                DropdownQueryId = f.DropdownQueryId,
                DropdownQueryValueColumn = f.DropdownQueryValueColumn,
                DropdownQueryLabelColumn = f.DropdownQueryLabelColumn
            })
            .ToList();

        dto.Tiles = dashboard.Tiles
            .OrderBy(t => t.SortOrder)
            .Select(t => new DashboardTileDto
            {
                Id = t.Id,
                Title = t.Title,
                DynamicQueryId = t.DynamicQueryId,
                DynamicQueryName = t.DynamicQuery?.Name,
                SortOrder = t.SortOrder,
                Width = t.Width,
                Height = t.Height,
                VisualType = t.VisualType,
                CategoryColumn = t.CategoryColumn,
                SeriesColumns = ParseSeriesColumns(t.SeriesColumnsJson),
                MaxCategories = t.MaxCategories,
                ValueColumn = t.ValueColumn,
                CompareColumn = t.CompareColumn,
                KpiAggregate = t.KpiAggregate,
                ValueFormat = t.ValueFormat,
                TargetValue = t.TargetValue,
                TargetWarnPercent = t.TargetWarnPercent,
                ConditionalRules = ParseRules(t.ConditionalRulesJson),
                HigherIsBetter = t.HigherIsBetter,
                RefreshSeconds = t.RefreshSeconds,
                EffectiveRefreshSeconds = EffectiveRefreshSeconds(dashboard, t),
                DrillAction = t.DrillAction,
                DrillFilterName = t.DrillFilterId is { } filterId && filterNames.TryGetValue(filterId, out var name)
                    ? name
                    : null,
                DrillReportId = t.DrillReportId,
                DrillReportParameter = t.DrillReportParameter,
                ParameterMaps = t.ParameterMaps
                    .OrderBy(m => m.TargetParameterName)
                    .Select(m => new DashboardTileParameterMapDto
                    {
                        TargetParameterName = m.TargetParameterName,
                        SourceKind = m.SourceKind,
                        FilterName = m.DashboardFilterId is { } id && filterNames.TryGetValue(id, out var n) ? n : null,
                        ConstantValue = m.ConstantValue
                    })
                    .ToList(),
                AllowedExportFormats = ExportPermissions.Parse(t.DynamicQuery?.AllowedExportFormats)
                    .Select(f => f.ToString())
                    .ToList()
            })
            .ToList();

        return dto;
    }

    public static async Task<DashboardDto> LoadDtoAsync(
        IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken)
    {
        var dashboard = await unitOfWork.Dashboards.GetByIdAsync(id, cancellationToken, FullIncludes)
            ?? throw new NotFoundException(nameof(Dashboard), id);
        return ToDto(dashboard);
    }

    public static List<string> ParseSeriesColumns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    public static List<DashboardConditionalRule> ParseRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<DashboardConditionalRule>();

        try
        {
            return JsonSerializer.Deserialize<List<DashboardConditionalRule>>(json, JsonOptions)
                ?? new List<DashboardConditionalRule>();
        }
        catch (JsonException)
        {
            // Saved rules are validated on the way in, so this only guards against a hand edit.
            return new List<DashboardConditionalRule>();
        }
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static T Fill<T>(T dto, Dashboard dashboard) where T : DashboardSummaryDto
    {
        dto.Id = dashboard.Id;
        dto.Name = dashboard.Name;
        dto.Description = dashboard.Description ?? string.Empty;
        dto.IsEnabled = dashboard.IsEnabled;
        dto.DefaultRefreshSeconds = dashboard.DefaultRefreshSeconds;
        dto.SortOrder = dashboard.SortOrder;
        dto.TileCount = dashboard.Tiles.Count;
        dto.FilterCount = dashboard.Filters.Count;
        dto.CreatedAt = dashboard.CreatedAt;
        dto.UpdatedAt = dashboard.UpdatedAt;
        return dto;
    }
}
