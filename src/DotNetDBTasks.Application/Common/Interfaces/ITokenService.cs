using DotNetDBTasks.Domain.Entities;

namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>An issued access token and the moment it stops being valid.</summary>
/// <remarks>
/// Returned together on purpose. The expiry used to be recomputed by each caller — four copies
/// of <c>AddHours(1)</c> that had to agree with the one baked into the token itself — so a
/// change in one place quietly told clients the wrong thing. Now the token and the claim about
/// it come from the same call.
/// </remarks>
public record AccessToken(string Value, DateTime ExpiresAtUtc);

/// <summary>
/// Handles JWT token generation and validation.
///
/// <para>Token lifetimes are runtime settings (<c>session.accessTokenMinutes</c> /
/// <c>session.refreshTokenDays</c>), which is why issuing a token is asynchronous: it reads
/// them. Both are on sign-in paths, not hot paths.</para>
/// </summary>
public interface ITokenService
{
    Task<AccessToken> GenerateAccessTokenAsync(
        User user, IList<string> roles, CancellationToken cancellationToken = default);

    string GenerateRefreshToken();

    /// <summary>When a refresh token issued now should stop being accepted.</summary>
    Task<DateTime> GetRefreshTokenExpiryAsync(CancellationToken cancellationToken = default);

    Guid? GetUserIdFromExpiredToken(string token);
}
