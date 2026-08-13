namespace Bayan.Domain.Entities;

/// <summary>
/// One capability granted to one role — a row in the matrix on Settings → Permissions.
///
/// <para>
/// Absence means "not granted": there is no deny row, so a permission a role does not hold is
/// simply missing. That keeps the matrix a set rather than a three-state puzzle, and means a
/// newly added capability starts closed everywhere.
/// </para>
/// </summary>
public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    /// <summary>A value from <c>Permissions</c>. Stored as text, so it survives a reorder.</summary>
    public string Permission { get; set; } = string.Empty;
}
