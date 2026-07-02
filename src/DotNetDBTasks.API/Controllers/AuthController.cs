using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Application.Features.Auth.Commands;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Handles authentication operations including login, SSO, and token refresh.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly ILdapService _ldapService;

    public AuthController(
        IMediator mediator,
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        ILdapService ldapService)
    {
        _mediator = mediator;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _ldapService = ldapService;
    }

    /// <summary>
    /// Authenticates a user via Windows SSO (Kerberos/NTLM).
    /// The browser negotiates credentials automatically for domain-joined machines.
    /// If the user does not exist locally, they are auto-provisioned from Active Directory.
    /// </summary>
    /// <remarks>
    /// Uses the IIS-provided Windows scheme so SSO works under IIS in-process hosting,
    /// where IIS (not the Negotiate handler) performs Windows Authentication. Requires
    /// Windows Authentication enabled on the IIS site (see DEPLOY-AIRGAPPED.md).
    /// </remarks>
    [HttpGet("sso")]
    [Authorize(AuthenticationSchemes = IISDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Sso(CancellationToken cancellationToken)
    {
        var windowsIdentity = User.Identity;
        if (windowsIdentity is null || !windowsIdentity.IsAuthenticated)
            return Unauthorized("Windows authentication failed.");

        // Extract username from DOMAIN\username or username@domain format
        var windowsName = windowsIdentity.Name ?? string.Empty;
        var username = ExtractUsername(windowsName);

        if (string.IsNullOrEmpty(username))
            return Unauthorized("Could not determine username from Windows identity.");

        // Look up user in local database
        var users = await _unitOfWork.Users.FindAsync(
            u => u.Username == username && u.IsActive, cancellationToken);
        var user = users.FirstOrDefault();

        if (user is null)
        {
            // Auto-provision from Active Directory
            var ldapUser = await _ldapService.GetUserByUsernameAsync(username);
            if (ldapUser is null)
                return Unauthorized("User not found in Active Directory.");

            var userRole = (await _unitOfWork.Roles.FindAsync(
                r => r.Name == "User", cancellationToken)).FirstOrDefault();
            if (userRole is null)
                return StatusCode(500, "User role not found in the system.");

            user = new User
            {
                Id = Guid.NewGuid(),
                Username = ldapUser.Username,
                Email = ldapUser.Email,
                FirstName = ldapUser.FirstName,
                LastName = ldapUser.LastName,
                PasswordHash = "LDAP_AUTH",
                IsActive = true,
                AuthSource = AuthSource.Ldap,
                Department = ldapUser.Department,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Users.AddAsync(user, cancellationToken);
            await _unitOfWork.UserRoles.AddAsync(
                new UserRole { UserId = user.Id, RoleId = userRole.Id }, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else if (user.AuthSource == AuthSource.Ldap)
        {
            // Sync profile from AD on SSO login
            var ldapUser = await _ldapService.GetUserByUsernameAsync(username);
            if (ldapUser is not null)
            {
                user.Email = ldapUser.Email;
                user.FirstName = ldapUser.FirstName;
                user.LastName = ldapUser.LastName;
                user.Department = ldapUser.Department;
                _unitOfWork.Users.Update(user);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        // Issue JWT tokens
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

        return Ok(new AuthResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Username = user.Username,
            Roles = roleNames
        });
    }

    /// <summary>
    /// Authenticates a user with username and password and returns JWT tokens.
    /// Use this for admin login or when SSO is not available.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Refreshes an expired access token using a valid refresh token.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Extracts the username from Windows identity formats:
    /// DOMAIN\username -> username
    /// username@domain.com -> username
    /// </summary>
    private static string ExtractUsername(string windowsName)
    {
        if (windowsName.Contains('\\'))
            return windowsName.Split('\\').Last();

        if (windowsName.Contains('@'))
            return windowsName.Split('@').First();

        return windowsName;
    }
}
