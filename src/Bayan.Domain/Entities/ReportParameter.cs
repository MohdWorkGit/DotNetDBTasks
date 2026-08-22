using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// A single field on the report's run form. The point of the report-level parameter is
/// that the user fills "From date" once and it reaches every dataset that needs it —
/// the fan-out is described by <see cref="ReportParameterMap"/>.
///
/// <para>Deliberately mirrors <see cref="QueryParameter"/> field for field, including
/// the dropdown source configuration, so the existing Angular parameter controls and the
/// existing typed-coercion logic work unchanged against either.</para>
/// </summary>
public class ReportParameter : BaseEntity
{
    public Guid ReportId { get; set; }
    public Report Report { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public int SortOrder { get; set; }

    /// <summary>
    /// Multi-select. The value travels as a JSON array and each mapped query parameter
    /// expands it into <c>(@name_0, @name_1, ...)</c> for an IN clause, exactly as a
    /// multi-value <see cref="QueryParameter"/> does.
    /// </summary>
    public bool AllowMultiple { get; set; }

    // --- Dropdown-specific fields (only used when ParameterType == Dropdown) ---

    public DropdownSourceType? DropdownSourceType { get; set; }

    /// <summary>JSON array of static options: [{"label":"Display Text","value":"stored_value"}, ...]</summary>
    public string? DropdownStaticValues { get; set; }

    /// <summary>
    /// ID of the DynamicQuery whose results populate the options. No FK constraint —
    /// resolved at runtime, same as <see cref="QueryParameter.DropdownQueryId"/>.
    /// </summary>
    public Guid? DropdownQueryId { get; set; }

    public string? DropdownQueryValueColumn { get; set; }
    public string? DropdownQueryLabelColumn { get; set; }

    public ICollection<ReportParameterMap> Maps { get; set; } = new List<ReportParameterMap>();
}
