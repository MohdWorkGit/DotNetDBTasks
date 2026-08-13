using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.Auth.Commands;

/// <summary>
/// Authenticates a user and returns JWT tokens.
/// Tries LDAP authentication first for provisioned LDAP users, falls back to local auth.
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
    private readonly ILdapService _ldapService;

    public LoginCommandHandler(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ILdapService ldapService)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _ldapService = ldapService;
    }

    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        // Look up the user in the local database
        var users = await _unitOfWork.Users.FindAsync(
            u => u.Username == request.Username && u.IsActive, cancellationToken);
        var user = users.FirstOrDefault();

        if (user is not null && user.AuthSource == AuthSource.Ldap)
        {
            // LDAP user: authenticate against the directory
            var ldapResult = await _ldapService.AuthenticateAsync(request.Username, request.Password);
            if (ldapResult is null)
                throw new UnauthorizedAccessException("Invalid username or password.");

            // Sync profile from LDAP on each login
            user.Email = ldapResult.Email;
            user.FirstName = ldapResult.FirstName;
            user.LastName = ldapResult.LastName;
            user.Department = ldapResult.Department;
            _unitOfWork.Users.Update(user);
        }
        else if (user is not null && user.AuthSource == AuthSource.Local)
        {
            // Local user: verify password hash
            if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
                throw new UnauthorizedAccessException("Invalid username or password.");
        }
        else
        {
            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        return await GenerateTokens(user, cancellationToken);
    }

    private async Task<AuthResult> GenerateTokens(User user, CancellationToken cancellationToken)
    {
        var userRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == user.Id, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();

        var roles = await _unitOfWork.Roles.FindAsync(
            r => roleIds.Contains(r.Id), cancellationToken);
        var roleNames = roles.Select(r => r.Name).ToList();

        var accessToken = await _tokenService.GenerateAccessTokenAsync(user, roleNames, cancellationToken);
        var refreshToken = _tokenService.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = await _tokenService.GetRefreshTokenExpiryAsync(cancellationToken);
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult
        {
            AccessToken = accessToken.Value,
            RefreshToken = refreshToken,
            ExpiresAt = accessToken.ExpiresAtUtc,
            Username = user.Username,
            Roles = roleNames
        };
    }
}
