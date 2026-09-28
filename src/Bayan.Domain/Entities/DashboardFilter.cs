using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// A control in the dashboard's filter bar. Its value reaches whichever tiles map it onto one of
/// their query's parameters, so "Branch" chosen once re-queries every tile that cares about it.
///
/// <para>Mirrors <see cref="ReportParameter"/> field for field, dropdown configuration included,
/// so the same typing and the same client controls work against either.</para>
/// </summary>
public class DashboardFilter : BaseEntity
{
    public Guid DashboardId { get; set; }
    public Dashboard Dashboard { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public int SortOrder { get; set; }
    public bool AllowMultiple { get; set; }

    public DropdownSourceType? DropdownSourceType { get; set; }

    /// <summary>JSON array of static options: [{"label":"Display Text","value":"stored_value"}, ...]</summary>
    public string? DropdownStaticValues { get; set; }

    /// <summary>The lookup query for the options. No FK, resolved at run time.</summary>
    public Guid? DropdownQueryId { get; set; }

    public string? DropdownQueryValueColumn { get; set; }
    public string? DropdownQueryLabelColumn { get; set; }
}
