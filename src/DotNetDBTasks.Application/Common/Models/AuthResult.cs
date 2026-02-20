namespace DotNetDBTasks.Application.Common.Models;

/// <summary>
/// Result model for authentication operations.
/// </summary>
public class AuthResult
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Username { get; set; } = string.Empty;
    public IList<string> Roles { get; set; } = new List<string>();
}
