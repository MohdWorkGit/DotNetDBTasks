namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Represents a database connection credential that can be assigned to dynamic queries.
/// Credentials (password) are stored encrypted at rest using AES-256.
/// </summary>
public class DatabaseUser : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1521;
    public string ServiceName { get; set; } = string.Empty;
    public string DbUsername { get; set; } = string.Empty;

    /// <summary>
    /// AES-256-CBC encrypted password. Never stored in plain text.
    /// </summary>
    public string EncryptedPassword { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<UserDatabaseUserAccess> UserAccess { get; set; } = new List<UserDatabaseUserAccess>();
    public ICollection<DynamicQuery> DynamicQueries { get; set; } = new List<DynamicQuery>();
}
