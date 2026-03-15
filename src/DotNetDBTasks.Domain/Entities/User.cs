using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Represents a system user with authentication credentials and role assignments.
/// </summary>
public class User : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public AuthSource AuthSource { get; set; } = AuthSource.Local;
    public string? Department { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<QueryExecutionLog> QueryExecutionLogs { get; set; } = new List<QueryExecutionLog>();
    public ICollection<UserDatabaseUserAccess> DatabaseUserAccess { get; set; } = new List<UserDatabaseUserAccess>();
}
