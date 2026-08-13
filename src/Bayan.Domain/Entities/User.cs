using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// Represents a system user with authentication credentials and role assignments.
/// </summary>
public class User : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    /// <summary>
    /// Optional. Null when no address is known — a locally created user may be given one
    /// later, and an AD account simply may not publish a <c>mail</c> attribute. Stored NULL
    /// rather than "": the column carries a unique index, and Oracle permits many NULLs in
    /// one but would reject a second empty string.
    /// </summary>
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public AuthSource AuthSource { get; set; } = AuthSource.Local;

    /// <summary>
    /// The account's department as published by Active Directory, recorded on import and
    /// refreshed on sync/login. Directory metadata only — it is shown on the AD page and
    /// grants nothing. Access is granted through <see cref="UserGroup"/> membership, which
    /// this application owns.
    /// </summary>
    public string? Department { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<QueryExecutionLog> QueryExecutionLogs { get; set; } = new List<QueryExecutionLog>();
}
