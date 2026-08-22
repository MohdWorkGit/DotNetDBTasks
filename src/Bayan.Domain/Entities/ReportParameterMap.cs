using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// Supplies one value to one of a dataset query's declared parameters when the report runs.
/// This is the piece that turns N separate parameter forms into one: "From date" asked once
/// on the report form can feed <c>@startDate</c> on the sales query and <c>@from</c> on the
/// returns query at the same time.
///
/// <para>The value comes from whichever source <see cref="SourceKind"/> names — the report's
/// own form, a constant the user is never asked for, or a column of the parent row when this
/// dataset is a master/detail child.</para>
///
/// <para>A query parameter with no map falls back to its own
/// <see cref="QueryParameter.DefaultValue"/>, so a dataset never fails merely because the
/// report author did not wire every one of its parameters.</para>
/// </summary>
public class ReportParameterMap : BaseEntity
{
    public Guid ReportDatasetId { get; set; }
    public ReportDataset ReportDataset { get; set; } = null!;

    /// <summary>Name of the target query's declared parameter, without the leading @.</summary>
    public string TargetParameterName { get; set; } = string.Empty;

    public ReportParameterSourceKind SourceKind { get; set; }

    /// <summary>
    /// The report-level field feeding this parameter. Set only when
    /// <see cref="SourceKind"/> is <see cref="ReportParameterSourceKind.ReportParameter"/>.
    /// </summary>
    public Guid? ReportParameterId { get; set; }
    public ReportParameter? ReportParameter { get; set; }

    /// <summary>
    /// The fixed value. Set only when <see cref="SourceKind"/> is
    /// <see cref="ReportParameterSourceKind.Constant"/>.
    /// </summary>
    public string? ConstantValue { get; set; }

    /// <summary>
    /// Column on the parent dataset's current row. Set only when <see cref="SourceKind"/> is
    /// <see cref="ReportParameterSourceKind.ParentColumn"/>.
    /// </summary>
    public string? ParentColumn { get; set; }
}
