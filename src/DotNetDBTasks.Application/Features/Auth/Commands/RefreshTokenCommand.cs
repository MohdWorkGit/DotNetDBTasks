using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.Auth.Commands;

/// <summary>
/// Refreshes an expired access token using a valid refresh token.
/// </summary>
public class RefreshTokenCommand : IRequest<AuthResult>
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;

    public RefreshTokenCommandHandler(IUnitOfWork unitOfWork, ITokenService tokenService)
    {
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
    }

    public async Task<AuthResult> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var userId = _tokenService.GetUserIdFromExpiredToken(request.AccessToken);
        if (userId is null)
            throw new UnauthorizedAccessException("Invalid access token.");

        var user = await _unitOfWork.Users.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null ||
            user.RefreshToken != request.RefreshToken ||
            user.RefreshTokenExpiryTime <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");
        }

        var userRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == user.Id, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();
        var roles = await _unitOfWork.Roles.FindAsync(
            r => roleIds.Contains(r.Id), cancellationToken);
        var roleNames = roles.Select(r => r.Name).ToList();

        var newAccessToken = await _tokenService.GenerateAccessTokenAsync(user, roleNames, cancellationToken);
        var newRefreshToken = _tokenService.GenerateRefreshToken();

        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = await _tokenService.GetRefreshTokenExpiryAsync(cancellationToken);
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult
        {
            AccessToken = newAccessToken.Value,
            RefreshToken = newRefreshToken,
            ExpiresAt = newAccessToken.ExpiresAtUtc,
            Username = user.Username,
            Roles = roleNames
        };
    }
}
