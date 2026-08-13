using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;
using Bayan.Application.Features.Auth.Commands;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.AspNetCore.Mvc;

namespace Bayan.API.Controllers;

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
    private readonly IConfiguration _configuration;
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;

    public AuthController(
        IMediator mediator,
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        ILdapService ldapService,
        IConfiguration configuration,
        IAuthenticationSchemeProvider schemeProvider,
        ICurrentUserService currentUser,
        IPermissionService permissions)
    {
        _currentUser = currentUser;
        _permissions = permissions;
        _mediator = mediator;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _ldapService = ldapService;
        _configuration = configuration;
        _schemeProvider = schemeProvider;
    }

    /// <summary>
    /// Authenticates a user via Windows SSO (Kerberos/NTLM).
    /// The browser negotiates credentials automatically for domain-joined machines.
    /// If the user does not exist locally, they are auto-provisioned from Active Directory.
    /// </summary>
    /// <remarks>
    /// Disabled unless Auth:EnableSso is true — deployments using form-based AD login
    /// (e.g. Kestrel behind nginx) don't carry a half-configured Windows-auth endpoint.
    /// When enabled, the Windows handshake uses the IIS-provided scheme under IIS
    /// in-process hosting (where IIS performs Windows Authentication) and falls back to
    /// the Negotiate handler when self-hosted on Kestrel/HTTP.sys.
    /// </remarks>
    [HttpGet("sso")]
    [AllowAnonymous]
    public async Task<IActionResult> Sso(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("Auth:EnableSso", false))
            return NotFound();

        // IIS in-process registers the "Windows" scheme; anywhere else use Negotiate.
        var scheme = await _schemeProvider.GetSchemeAsync(IISDefaults.AuthenticationScheme) is not null
            ? IISDefaults.AuthenticationScheme
            : NegotiateDefaults.AuthenticationScheme;

        var auth = await HttpContext.AuthenticateAsync(scheme);
        if (!auth.Succeeded)
            return Challenge(scheme);

        var windowsIdentity = auth.Principal.Identity;
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

        var accessToken = await _tokenService.GenerateAccessTokenAsync(user, roleNames, cancellationToken);
        var refreshToken = _tokenService.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = await _tokenService.GetRefreshTokenExpiryAsync(cancellationToken);
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(new AuthResult
        {
            AccessToken = accessToken.Value,
            RefreshToken = refreshToken,
            ExpiresAt = accessToken.ExpiresAtUtc,
            Username = user.Username,
            Roles = roleNames
        });
    }

    /// <summary>
    /// Authenticates a user with username and password and returns JWT tokens.
    /// Use this for admin login or when SSO is not available.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
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
    /// Who the caller is and what they may currently do.
    ///
    /// <para>The client asks on every load rather than reading the token, because permissions
    /// are edited at runtime and the token is not reissued when they change. A tab left open
    /// while a role is re-permissioned picks the change up on its next navigation, and until
    /// then the server refuses anything it should — this only decides what the UI offers.</para>
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var roles = _currentUser.Roles;
        var permissions = await _permissions.GetForRolesAsync(roles, cancellationToken);

        return Ok(new
        {
            username = _currentUser.Username,
            roles,
            permissions = permissions.OrderBy(p => p, StringComparer.Ordinal).ToList()
        });
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
