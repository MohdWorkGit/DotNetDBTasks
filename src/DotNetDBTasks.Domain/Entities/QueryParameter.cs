using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Defines a parameter for a dynamic query, including its type and validation constraints.
/// Used to generate dynamic input forms on the frontend.
/// When ParameterType is Dropdown, the dropdown source configuration fields apply.
/// </summary>
public class QueryParameter : BaseEntity
{
    public Guid DynamicQueryId { get; set; }
    public DynamicQuery DynamicQuery { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public int SortOrder { get; set; }

    // --- Dropdown-specific fields (only used when ParameterType == Dropdown) ---

    /// <summary>
    /// Specifies whether dropdown options come from a static list or a database query.
    /// </summary>
    public DropdownSourceType? DropdownSourceType { get; set; }

    /// <summary>
    /// JSON array of static options: [{"label":"Display Text","value":"stored_value"}, ...]
    /// Used when DropdownSourceType == Static.
    /// </summary>
    public string? DropdownStaticValues { get; set; }

    /// <summary>
    /// ID of the DynamicQuery whose results populate the dropdown options.
    /// Used when DropdownSourceType == Query.
    /// </summary>
    public Guid? DropdownQueryId { get; set; }

    /// <summary>
    /// Navigation property to the lookup query.
    /// </summary>
    public DynamicQuery? DropdownQuery { get; set; }

    /// <summary>
    /// Column name in the lookup query result set to use as the option value.
    /// Used when DropdownSourceType == Query.
    /// </summary>
    public string? DropdownQueryValueColumn { get; set; }

    /// <summary>
    /// Column name in the lookup query result set to use as the option label.
    /// Used when DropdownSourceType == Query.
    /// </summary>
    public string? DropdownQueryLabelColumn { get; set; }
}
