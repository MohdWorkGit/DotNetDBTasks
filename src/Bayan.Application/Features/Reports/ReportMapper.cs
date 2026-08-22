using Bayan.Application.Common.Interfaces;
using Bayan.Application.Features.Reports.Dtos;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using Bayan.Domain.Services;

namespace Bayan.Application.Features.Reports;

/// <summary>
/// Turns report entities into the DTOs the client reads. Kept in one place so the admin
/// builder, the run form and the list cannot drift into describing the same report differently.
/// </summary>
public static class ReportMapper
{
    /// <summary>
    /// The include paths a full <see cref="ReportDto"/> needs. Separate entries, not one
    /// comma-joined string — the repository passes each to its own <c>Include</c> call.
    /// </summary>
    public static readonly string[] FullIncludes =
    {
        "QueryGroup",
        "Datasets",
        "Datasets.DynamicQuery",
        "Datasets.ParameterMaps",
        "Datasets.ParameterMaps.ReportParameter",
        "Parameters",
        "Charts"
    };

    public static async Task<ReportDto> LoadDtoAsync(
        IUnitOfWork unitOfWork,
        Guid reportId,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? heldPermissions = null)
    {
        var report = await unitOfWork.Reports.GetByIdAsync(reportId, cancellationToken, FullIncludes)
            ?? throw new NotFoundException(nameof(Report), reportId);

        return ToDto(report, heldPermissions);
    }

    public static ReportDto ToDto(Report report, IReadOnlySet<string>? heldPermissions)
    {
        var datasetKeysById = report.Datasets.ToDictionary(d => d.Id, d => d.DatasetKey);

        var dto = new ReportDto
        {
            TimeoutSeconds = report.TimeoutSeconds,
            MaxDetailRows = report.MaxDetailRows,
            MaxTotalRows = report.MaxTotalRows,
            Datasets = report.Datasets
                .OrderBy(d => d.SortOrder)
                .ThenBy(d => d.DatasetKey)
                .Select(d => ToDatasetDto(d, datasetKeysById))
                .ToList(),
            Parameters = report.Parameters
                .OrderBy(p => p.SortOrder)
                .Select(ToParameterDto)
                .ToList(),
            Charts = report.Charts
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.ChartKey)
                .Select(c => ToChartDto(c, datasetKeysById))
                .ToList()
        };

        FillSummary(dto, report, heldPermissions);
        return dto;
    }

    public static ReportSummaryDto ToSummary(Report report, IReadOnlySet<string>? heldPermissions)
    {
        var dto = new ReportSummaryDto();
        FillSummary(dto, report, heldPermissions);
        return dto;
    }

    private static void FillSummary(
        ReportSummaryDto dto,
        Report report,
        IReadOnlySet<string>? heldPermissions)
    {
        dto.Id = report.Id;
        dto.Name = report.Name;
        dto.Description = report.Description ?? string.Empty;
        dto.IsEnabled = report.IsEnabled;
        dto.DatasetCount = report.Datasets.Count;
        dto.ParameterCount = report.Parameters.Count;
        dto.HasTemplate = report.TemplateDocx is { Length: > 0 };
        dto.TemplateFileName = report.TemplateFileName;
        dto.QueryGroupId = report.QueryGroupId;
        dto.QueryGroupName = report.QueryGroup?.Name;
        dto.CreatedAt = report.CreatedAt;
        dto.UpdatedAt = report.UpdatedAt;

        // The formats the caller may actually download: what the report permits, narrowed by
        // what their roles permit. Sending the already-narrowed list means the client's export
        // menu cannot offer something the API would refuse — though the API re-checks anyway,
        // because hiding a menu item is not a control.
        dto.AllowedExportFormats = heldPermissions is null
            ? ExportPermissions.Parse(report.AllowedExportFormats).Select(f => f.ToString()).ToList()
            : ExportPermissions
                .EffectiveForReport(report.AllowedExportFormats, heldPermissions.Contains)
                .Select(f => f.ToString())
                .ToList();
    }

    private static ReportDatasetDto ToDatasetDto(ReportDataset dataset, IReadOnlyDictionary<Guid, string> keysById) => new()
    {
        ParentDatasetKey = dataset.ParentDatasetId is not null
            && keysById.TryGetValue(dataset.ParentDatasetId.Value, out var parentKey) ? parentKey : null,
        LeftDatasetKey = dataset.LeftDatasetId is not null
            && keysById.TryGetValue(dataset.LeftDatasetId.Value, out var leftKey) ? leftKey : null,
        RightDatasetKey = dataset.RightDatasetId is not null
            && keysById.TryGetValue(dataset.RightDatasetId.Value, out var rightKey) ? rightKey : null,
        JoinType = dataset.JoinType,
        LeftColumn = dataset.LeftColumn,
        RightColumn = dataset.RightColumn,
        Id = dataset.Id,
        DatasetKey = dataset.DatasetKey,
        DisplayName = dataset.DisplayName,
        SourceType = dataset.SourceType,
        SortOrder = dataset.SortOrder,
        IsVisibleInViewer = dataset.IsVisibleInViewer,
        DynamicQueryId = dataset.DynamicQueryId,
        DynamicQueryName = dataset.DynamicQuery?.Name,
        ParameterMaps = dataset.ParameterMaps.Select(m => new ReportParameterMapDto
        {
            Id = m.Id,
            TargetParameterName = m.TargetParameterName,
            SourceKind = m.SourceKind,
            ReportParameterId = m.ReportParameterId,
            ConstantValue = m.ConstantValue,
            ParentColumn = m.ParentColumn
        }).ToList()
    };

    private static ReportChartDto ToChartDto(ReportChart chart, IReadOnlyDictionary<Guid, string> keysById) => new()
    {
        Id = chart.Id,
        ChartKey = chart.ChartKey,
        Title = chart.Title,
        ChartType = chart.ChartType,
        DatasetKey = keysById.TryGetValue(chart.DatasetId, out var key) ? key : null,
        CategoryColumn = chart.CategoryColumn,
        SeriesColumns = ParseSeriesColumns(chart.SeriesColumnsJson),
        MaxCategories = chart.MaxCategories,
        SortOrder = chart.SortOrder
    };

    private static List<string> ParseSeriesColumns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new List<string>();
        }
    }

    private static ReportParameterDto ToParameterDto(ReportParameter parameter) => new()
    {
        Id = parameter.Id,
        Name = parameter.Name,
        DisplayName = parameter.DisplayName,
        ParameterType = parameter.ParameterType,
        IsRequired = parameter.IsRequired,
        DefaultValue = parameter.DefaultValue,
        SortOrder = parameter.SortOrder,
        AllowMultiple = parameter.AllowMultiple,
        DropdownSourceType = parameter.DropdownSourceType,
        DropdownStaticValues = parameter.DropdownStaticValues,
        DropdownQueryId = parameter.DropdownQueryId,
        DropdownQueryValueColumn = parameter.DropdownQueryValueColumn,
        DropdownQueryLabelColumn = parameter.DropdownQueryLabelColumn
    };

    /// <summary>
    /// The report's charts as the starter-template generator needs them, so a generated template
    /// carries a {{CHART:key}} marker for each — otherwise a report with a chart exports without
    /// it until somebody uploads a template by hand.
    /// </summary>
    public static IReadOnlyList<Common.Models.ReportTemplateChart> ToTemplateCharts(Report report)
    {
        var keysById = report.Datasets.ToDictionary(d => d.Id, d => d.DatasetKey);

        return report.Charts
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.ChartKey)
            .Select(c => new Common.Models.ReportTemplateChart(
                c.ChartKey,
                string.IsNullOrWhiteSpace(c.Title) ? string.Empty : c.Title!,
                keysById.TryGetValue(c.DatasetId, out var key) ? key : null))
            .ToList();
    }

    /// <summary>
    /// The report's datasets as the starter-template generator needs them: key plus heading,
    /// in the order they should appear in the document.
    /// </summary>
    public static IReadOnlyList<Common.Models.ReportTemplateSection> ToTemplateSections(Report report)
    {
        var keysById = report.Datasets.ToDictionary(d => d.Id, d => d.DatasetKey);

        return report.Datasets
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.DatasetKey)
            .Select(d => new Common.Models.ReportTemplateSection(
                d.DatasetKey,
                string.IsNullOrWhiteSpace(d.DisplayName) ? d.DatasetKey : d.DisplayName,
                d.ParentDatasetId is not null && keysById.TryGetValue(d.ParentDatasetId.Value, out var parentKey)
                    ? parentKey
                    : null))
            .ToList();
    }
}
