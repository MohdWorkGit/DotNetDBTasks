using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.Auth.Commands;

/// <summary>
/// Authenticates a user and returns JWT tokens.
/// </summary>
public class LoginCommand : IRequest<AuthResult>
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public LoginCommandHandler(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokenService)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var users = await _unitOfWork.Users.FindAsync(
            u => u.Username == request.Username && u.IsActive, cancellationToken);
        var user = users.FirstOrDefault();

        if (user is null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid username or password.");

        var userRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == user.Id, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();

        var roles = await _unitOfWork.Roles.FindAsync(
            r => roleIds.Contains(r.Id), cancellationToken);
        var roleNames = roles.Select(r => r.Name).ToList();

        var accessToken = _tokenService.GenerateAccessToken(user, roleNames);
        var refreshToken = _tokenService.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Username = user.Username,
            Roles = roleNames
        };
    }
}
