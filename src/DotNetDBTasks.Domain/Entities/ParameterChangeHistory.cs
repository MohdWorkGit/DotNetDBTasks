namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Tracks old and new values when a query parameter is added, modified, or removed.
/// One record per changed field per parameter per update operation.
/// </summary>
public class ParameterChangeHistory : BaseEntity
{
    public Guid DynamicQueryId { get; set; }
    public DynamicQuery DynamicQuery { get; set; } = null!;

    /// <summary>
    /// The parameter name that was changed (identifies the parameter within the query).
    /// </summary>
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>
    /// The type of change: Added, Modified, or Removed.
    /// </summary>
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>
    /// The field/property that changed (e.g. "DefaultValue", "DisplayName", "ParameterType").
    /// For Added/Removed changes this is "Parameter".
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// The previous value (null for newly added parameters).
    /// </summary>
    public string? OldValue { get; set; }

    /// <summary>
    /// The new value (null for removed parameters).
    /// </summary>
    public string? NewValue { get; set; }

    /// <summary>
    /// When the change occurred.
    /// </summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>
    /// The user who made the change (populated from the current authenticated user).
    /// </summary>
    public Guid? ChangedByUserId { get; set; }
}
