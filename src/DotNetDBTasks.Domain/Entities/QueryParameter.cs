using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Defines a parameter for a dynamic query, including its type and validation constraints.
/// Used to generate dynamic input forms on the frontend.
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
}
