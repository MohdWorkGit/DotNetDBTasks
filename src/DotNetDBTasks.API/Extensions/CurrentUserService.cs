using System.Security.Claims;
using DotNetDBTasks.Application.Common.Interfaces;

namespace DotNetDBTasks.API.Extensions;

/// <summary>
/// Extracts current user information from the HTTP context claims.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
        }
    }

    public string Username =>
        _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

    public IReadOnlyList<string> Roles =>
        _httpContextAccessor.HttpContext?.User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList() ?? new List<string>();

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
