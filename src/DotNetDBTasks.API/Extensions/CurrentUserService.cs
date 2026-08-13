using System.Security.Claims;
using DotNetDBTasks.Application.Common.Interfaces;

namespace DotNetDBTasks.API.Extensions;

/// <summary>
/// Extracts current user information from the HTTP context claims. When there is no
/// authenticated HTTP context (e.g. the background query worker running off-thread),
/// it falls back to the ambient <see cref="IUserExecutionContext"/> snapshot.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserExecutionContext _userExecutionContext;

    public CurrentUserService(
        IHttpContextAccessor httpContextAccessor,
        IUserExecutionContext userExecutionContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _userExecutionContext = userExecutionContext;
    }

    private ClaimsPrincipal? HttpUser
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.Identity?.IsAuthenticated == true ? user : null;
        }
    }

    public Guid UserId
    {
        get
        {
            var claim = HttpUser?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(claim, out var id))
                return id;
            return _userExecutionContext.Current?.UserId ?? Guid.Empty;
        }
    }

    public string Username =>
        HttpUser?.FindFirst(ClaimTypes.Name)?.Value
        ?? _userExecutionContext.Current?.Username
        ?? string.Empty;

    public IReadOnlyList<string> Roles =>
        HttpUser?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList()
        ?? _userExecutionContext.Current?.Roles
        ?? new List<string>();

    public bool IsAuthenticated =>
        HttpUser is not null || _userExecutionContext.Current is not null;
}
