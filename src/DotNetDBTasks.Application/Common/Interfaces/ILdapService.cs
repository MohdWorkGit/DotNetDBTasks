namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Provides LDAP/Active Directory operations for user lookup and authentication.
/// </summary>
public interface ILdapService
{
    /// <summary>
    /// Authenticates a user against LDAP using their username and password.
    /// Returns the LDAP user info if successful, null otherwise.
    /// </summary>
    Task<LdapUserInfo?> AuthenticateAsync(string username, string password);

    /// <summary>
    /// Looks up a single user in LDAP by exact username match.
    /// Returns the user info if found, null otherwise.
    /// </summary>
    Task<LdapUserInfo?> GetUserByUsernameAsync(string username);

    /// <summary>
    /// Searches for users in LDAP by partial username, name, or email.
    /// </summary>
    Task<IReadOnlyList<LdapUserInfo>> SearchUsersAsync(string searchTerm);

    /// <summary>
    /// Returns all distinct departments from LDAP.
    /// </summary>
    Task<IReadOnlyList<string>> GetDepartmentsAsync();

    /// <summary>
    /// Returns all users belonging to a specific department.
    /// </summary>
    Task<IReadOnlyList<LdapUserInfo>> GetUsersByDepartmentAsync(string department);
}

public class LdapUserInfo
{
    public string Username { get; set; } = string.Empty;
    /// <summary>The directory's <c>mail</c> attribute, or null when the account has none.</summary>
    public string? Email { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Department { get; set; }
}
