namespace Bayan.Domain.Enums;

/// <summary>
/// Defines where a dropdown parameter gets its list of options from.
/// </summary>
public enum DropdownSourceType
{
    /// <summary>Static list of label/value pairs defined by the admin.</summary>
    Static = 0,

    /// <summary>Options are loaded at runtime by executing another query from the database.</summary>
    Query = 1
}
