using Bayan.Domain.Enums;

namespace Bayan.Application.Features.Reports.Dtos;

/// <summary>One row of the admin report list, and of the user's "My Reports" list.</summary>
public class ReportSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int DatasetCount { get; set; }
    public int ParameterCount { get; set; }
    public bool HasTemplate { get; set; }

    /// <summary>The query group this report is filed in — the same folders queries use.</summary>
    public Guid? QueryGroupId { get; set; }
    public string? QueryGroupName { get; set; }
    public string? TemplateFileName { get; set; }

    /// <summary>
    /// Formats this report may be exported as, already narrowed to the ones the caller's roles
    /// permit — so the client renders its download menu from this and never offers something
    /// the API would refuse.
    /// </summary>
    public List<string> AllowedExportFormats { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>A report's full definition, for the builder and for the run form.</summary>
public class ReportDto : ReportSummaryDto
{
    public int TimeoutSeconds { get; set; }
    public int MaxDetailRows { get; set; }
    public int MaxTotalRows { get; set; }
    public List<ReportDatasetDto> Datasets { get; set; } = new();
    public List<ReportParameterDto> Parameters { get; set; } = new();
    public List<ReportChartDto> Charts { get; set; } = new();
}

/// <summary>A chart the template can place with a {{CHART:key}} marker.</summary>
public class ReportChartDto
{
    public Guid Id { get; set; }
    public string ChartKey { get; set; } = string.Empty;
    public string? Title { get; set; }
    public ReportChartType ChartType { get; set; }

    /// <summary>The dataset it draws from, by key — the builder works in keys, not ids.</summary>
    public string? DatasetKey { get; set; }

    public string CategoryColumn { get; set; } = string.Empty;
    public List<string> SeriesColumns { get; set; } = new();
    public int MaxCategories { get; set; }
    public int SortOrder { get; set; }
}

public class ReportDatasetDto
{
    public Guid Id { get; set; }
    public string DatasetKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ReportDatasetSourceType SourceType { get; set; }
    public int SortOrder { get; set; }
    public bool IsVisibleInViewer { get; set; }

    public Guid? DynamicQueryId { get; set; }

    /// <summary>Shown in the builder list so a dataset is identifiable without opening it.</summary>
    public string? DynamicQueryName { get; set; }

    /// <summary>For a Detail dataset, the key of the dataset it repeats under.</summary>
    public string? ParentDatasetKey { get; set; }

    // --- Join source: the two datasets combined, and the columns matched on.
    public string? LeftDatasetKey { get; set; }
    public string? RightDatasetKey { get; set; }
    public ReportJoinType? JoinType { get; set; }
    public string? LeftColumn { get; set; }
    public string? RightColumn { get; set; }

    public List<ReportParameterMapDto> ParameterMaps { get; set; } = new();
}

public class ReportParameterMapDto
{
    public Guid Id { get; set; }
    public string TargetParameterName { get; set; } = string.Empty;
    public ReportParameterSourceKind SourceKind { get; set; }
    public Guid? ReportParameterId { get; set; }
    public string? ConstantValue { get; set; }
    public string? ParentColumn { get; set; }
}

/// <summary>
/// Mirrors the shape of a query parameter so the client can reuse the same controls it already
/// renders on the query run form.
/// </summary>
public class ReportParameterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public int SortOrder { get; set; }
    public bool AllowMultiple { get; set; }
    public DropdownSourceType? DropdownSourceType { get; set; }
    public string? DropdownStaticValues { get; set; }
    public Guid? DropdownQueryId { get; set; }
    public string? DropdownQueryValueColumn { get; set; }
    public string? DropdownQueryLabelColumn { get; set; }
}

/// <summary>Who a report is granted to, for the access page.</summary>
public class ReportAccessDto
{
    public List<Guid> RoleIds { get; set; } = new();
    public List<Guid> UserGroupIds { get; set; } = new();
    public List<Guid> UserIds { get; set; } = new();
}

/// <summary>
/// What the builder sends when saving. The whole graph is replaced on save rather than diffed:
/// the datasets, parameters and maps are one design that only makes sense together, and a
/// partial update is how a report ends up with a map pointing at a parameter that no longer
/// exists.
/// </summary>
public class ReportInput
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public Guid? QueryGroupId { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxDetailRows { get; set; } = 100;
    public int MaxTotalRows { get; set; } = 200_000;
    public List<string> AllowedExportFormats { get; set; } = new();
    public List<ReportDatasetInput> Datasets { get; set; } = new();
    public List<ReportParameterInput> Parameters { get; set; } = new();
    public List<ReportChartInput> Charts { get; set; } = new();
}

public class ReportChartInput
{
    public string ChartKey { get; set; } = string.Empty;
    public string? Title { get; set; }
    public ReportChartType ChartType { get; set; }

    /// <summary>The dataset key it draws from; resolved to an id on save.</summary>
    public string DatasetKey { get; set; } = string.Empty;

    public string CategoryColumn { get; set; } = string.Empty;
    public List<string> SeriesColumns { get; set; } = new();
    public int MaxCategories { get; set; } = 25;
    public int SortOrder { get; set; }
}

public class ReportDatasetInput
{
    /// <summary>
    /// The builder's local handle for this dataset. Not a database id — it exists so a
    /// parameter map can name its dataset before either has been saved.
    /// </summary>
    public string DatasetKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public ReportDatasetSourceType SourceType { get; set; } = ReportDatasetSourceType.Query;
    public int SortOrder { get; set; }
    public bool IsVisibleInViewer { get; set; } = true;
    public Guid? DynamicQueryId { get; set; }

    /// <summary>
    /// For a Detail dataset, the <b>key</b> of the dataset it repeats under — not an id, because
    /// the builder can create a parent and its child in the same save, before either has one.
    /// </summary>
    public string? ParentDatasetKey { get; set; }

    // --- Join source, by key for the same reason the parent is.
    public string? LeftDatasetKey { get; set; }
    public string? RightDatasetKey { get; set; }
    public ReportJoinType? JoinType { get; set; }
    public string? LeftColumn { get; set; }
    public string? RightColumn { get; set; }

    public List<ReportParameterMapInput> ParameterMaps { get; set; } = new();
}

public class ReportParameterMapInput
{
    public string TargetParameterName { get; set; } = string.Empty;
    public ReportParameterSourceKind SourceKind { get; set; }

    /// <summary>
    /// The report parameter's <b>name</b>, not its id — the builder can create a parameter and
    /// map it in the same save, before that parameter has an id.
    /// </summary>
    public string? ReportParameterName { get; set; }

    public string? ConstantValue { get; set; }
    public string? ParentColumn { get; set; }
}

public class ReportParameterInput
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public int SortOrder { get; set; }
    public bool AllowMultiple { get; set; }
    public DropdownSourceType? DropdownSourceType { get; set; }
    public string? DropdownStaticValues { get; set; }
    public Guid? DropdownQueryId { get; set; }
    public string? DropdownQueryValueColumn { get; set; }
    public string? DropdownQueryLabelColumn { get; set; }
}

/// <summary>
/// What the client shows after a template upload: which markers the file uses, and how those
/// line up with the report's datasets.
/// </summary>
public class ReportTemplateInspectionDto
{
    public string? TemplateFileName { get; set; }

    /// <summary>Dataset keys the template renders, and that the report actually has.</summary>
    public List<string> MatchedDatasetKeys { get; set; } = new();

    /// <summary>Keys the template references that match no dataset — almost always a typo.</summary>
    public List<string> UnknownDatasetKeys { get; set; } = new();

    /// <summary>Datasets with no marker, which will run but appear nowhere in the document.</summary>
    public List<string> DatasetsWithoutMarker { get; set; } = new();

    /// <summary>Keys marked twice inside one table, where only the first would render.</summary>
    public List<string> DuplicateKeysInOneTable { get; set; } = new();

    /// <summary>True when a bare {{RESULTS}} appends every dataset into a single table.</summary>
    public bool HasUnkeyedResultsMarker { get; set; }

    /// <summary>True when a placeholder sits in a text box, where it cannot be substituted.</summary>
    public bool HasPlaceholderInsideTextBox { get; set; }
}
