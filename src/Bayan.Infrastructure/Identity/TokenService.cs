using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Bayan.Infrastructure.Identity;

/// <summary>
/// JWT token generation and validation service.
/// </summary>
public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private readonly ISystemSettingsService _settings;

    public TokenService(IConfiguration configuration, ISystemSettingsService settings)
    {
        _configuration = configuration;
        _settings = settings;
    }

    public async Task<AccessToken> GenerateAccessTokenAsync(
        User user, IList<string> roles, CancellationToken cancellationToken = default)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            // ClaimTypes.Email is omitted entirely for users without an address —
            // Claim's constructor rejects a null value.
            new("FirstName", user.FirstName),
            new("LastName", user.LastName)
        };

        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["Jwt:Secret"]
                ?? throw new InvalidOperationException("JWT secret not configured.")));

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var minutes = await _settings.GetIntAsync(
            SystemSettingKeys.SessionAccessTokenMinutes,
            SystemSettingKeys.SessionAccessTokenMinutesDefault,
            cancellationToken);
        var expiresAt = DateTime.UtcNow.AddMinutes(minutes);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        // The same instant the token carries, so a client is never told it has longer than
        // the signature actually allows. JwtSecurityToken truncates to whole seconds.
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public async Task<DateTime> GetRefreshTokenExpiryAsync(CancellationToken cancellationToken = default)
    {
        var days = await _settings.GetIntAsync(
            SystemSettingKeys.SessionRefreshTokenDays,
            SystemSettingKeys.SessionRefreshTokenDaysDefault,
            cancellationToken);

        return DateTime.UtcNow.AddDays(days);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public Guid? GetUserIdFromExpiredToken(string token)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidIssuer = _configuration["Jwt:Issuer"],
            ValidAudience = _configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Secret"]!)),
            ValidateLifetime = false // Allow expired tokens for refresh
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);

        if (securityToken is not JwtSecurityToken jwtToken ||
            !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
        {
            return null;
        }

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
