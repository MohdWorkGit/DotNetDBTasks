using Bayan.Application.Features.Dashboards.Dtos;
using Bayan.Application.Features.DynamicQueries.Transfer;
using Bayan.Application.Features.Reports.Dtos;
using Bayan.Domain.Enums;

namespace Bayan.Application.Features.Transfer;

/// <summary>
/// The portable form of queries, reports and dashboards — what an export writes and an import
/// reads. Extends the query backup (<see cref="QueryExportFile"/>, format 1) rather than
/// replacing it: <see cref="Queries"/> has the same name and shape, so a format-1 query backup
/// reads as a transfer file with no reports or dashboards.
///
/// <para>As in the query backup, everything that points at another record travels as a
/// <b>name</b> — a query, a report, a role — never an id, and import re-resolves each name on
/// the target system. Inside one report or dashboard, links were already by key or name
/// (dataset keys, parameter and filter names), so those travel as they are.</para>
/// </summary>
public class TransferFile
{
    /// <summary>Bumped when the shape changes, so import can reject files it cannot read.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>2 added reports and dashboards; 1 is the query-only backup.</summary>
    public const int CurrentFormatVersion = 2;

    public DateTime ExportedAt { get; set; }
    public string? ExportedBy { get; set; }
    public List<ExportedQuery> Queries { get; set; } = new();
    public List<ExportedReport> Reports { get; set; } = new();
    public List<ExportedDashboard> Dashboards { get; set; } = new();
}

/// <summary>Who an item is granted to, by name. Import warns about and skips names it cannot find.</summary>
public abstract class ExportedGrants
{
    public List<string> AssignedRoles { get; set; } = new();
    public List<string> AssignedUserGroups { get; set; } = new();
    public List<string> AssignedUsers { get; set; } = new();
}

/// <summary>A report: <see cref="ReportInput"/> with its query and group references by name.</summary>
public class ExportedReport : ExportedGrants
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? QueryGroupName { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxDetailRows { get; set; } = 100;
    public int MaxTotalRows { get; set; } = 200_000;
    public List<string> AllowedExportFormats { get; set; } = new();

    public string? TemplateFileName { get; set; }
    /// <summary>Base64 of the .docx, so the imported report produces identical documents.</summary>
    public string? TemplateBase64 { get; set; }

    public List<ExportedReportDataset> Datasets { get; set; } = new();

    /// <summary>The run form's fields — the same shape as a query parameter, dropdown query by name.</summary>
    public List<ExportedParameter> Parameters { get; set; } = new();

    /// <summary>Charts reference their dataset by key already, so they travel as the builder sends them.</summary>
    public List<ReportChartInput> Charts { get; set; } = new();
}

public class ExportedReportDataset
{
    public string DatasetKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ReportDatasetSourceType SourceType { get; set; }
    public int SortOrder { get; set; }
    public bool IsVisibleInViewer { get; set; } = true;

    /// <summary>The query that produces the rows; null for a join.</summary>
    public string? DynamicQueryName { get; set; }

    public string? ParentDatasetKey { get; set; }
    public string? LeftDatasetKey { get; set; }
    public string? RightDatasetKey { get; set; }
    public ReportJoinType? JoinType { get; set; }
    public string? LeftColumn { get; set; }
    public string? RightColumn { get; set; }

    /// <summary>Already by name: a report parameter's name, a constant, or a parent column.</summary>
    public List<ReportParameterMapInput> ParameterMaps { get; set; } = new();
}

/// <summary>A dashboard: <see cref="DashboardInput"/> with query and report references by name.</summary>
public class ExportedDashboard : ExportedGrants
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int DefaultRefreshSeconds { get; set; } = 60;
    public int SortOrder { get; set; }

    /// <summary>The filter bar — the same shape as a query parameter, dropdown query by name.</summary>
    public List<ExportedParameter> Filters { get; set; } = new();

    public List<ExportedDashboardTile> Tiles { get; set; } = new();
}

public class ExportedDashboardTile
{
    public string Title { get; set; } = string.Empty;
    public string? DynamicQueryName { get; set; }
    public int SortOrder { get; set; }
    public int Width { get; set; } = 4;
    public int Height { get; set; } = 1;
    public DashboardVisualType VisualType { get; set; }
    public string? CategoryColumn { get; set; }
    public List<string> SeriesColumns { get; set; } = new();
    public int MaxCategories { get; set; } = 25;
    public string? ValueColumn { get; set; }
    public string? CompareColumn { get; set; }
    public DashboardKpiAggregate KpiAggregate { get; set; }
    public DashboardValueFormat ValueFormat { get; set; }
    public decimal? TargetValue { get; set; }
    public int TargetWarnPercent { get; set; } = 10;
    public List<DashboardConditionalRule> ConditionalRules { get; set; } = new();
    public DashboardDisplayOptions? DisplayOptions { get; set; }
    public bool HigherIsBetter { get; set; } = true;
    public int? RefreshSeconds { get; set; }
    public DashboardDrillAction DrillAction { get; set; }
    public string? DrillFilterName { get; set; }
    /// <summary>The report a click opens, by name.</summary>
    public string? DrillReportName { get; set; }
    public string? DrillReportParameter { get; set; }
    public List<DashboardTileParameterMapDto> ParameterMaps { get; set; } = new();
}

public enum TransferKind
{
    Report,
    Dashboard
}

/// <summary>What an import did, per kind, surfaced to the admin rather than silently applied.</summary>
public class TransferImportResult
{
    public List<ImportedQueryResult> Queries { get; set; } = new();
    public List<ImportedQueryResult> Reports { get; set; } = new();
    public List<ImportedQueryResult> Dashboards { get; set; } = new();

    /// <summary>Names that could not be resolved here, and items that were skipped, with why.</summary>
    public List<string> Warnings { get; set; } = new();

    public int ImportedCount => Queries.Count + Reports.Count + Dashboards.Count;
    public int RenamedCount =>
        Queries.Count(q => q.WasRenamed) + Reports.Count(r => r.WasRenamed) + Dashboards.Count(d => d.WasRenamed);
}
