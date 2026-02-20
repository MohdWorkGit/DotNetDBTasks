using DotNetDBTasks.Domain.Entities;

namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Handles JWT token generation and validation.
/// </summary>
public interface ITokenService
{
    string GenerateAccessToken(User user, IList<string> roles);
    string GenerateRefreshToken();
    Guid? GetUserIdFromExpiredToken(string token);
}
