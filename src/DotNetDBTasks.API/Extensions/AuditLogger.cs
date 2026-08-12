using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Interfaces;

namespace DotNetDBTasks.API.Extensions;

/// <summary>
/// Writes audit rows, resolving "who" and "from where" from the current request.
///
/// <para>
/// Lives in the API layer alongside <see cref="CurrentUserService"/> because the IP address
/// comes from <c>HttpContext</c>, which the Application layer must not know about.
/// </para>
/// </summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IHttpContextAccessor _httpContext;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IHttpContextAccessor httpContext,
        ILogger<AuditLogger> logger)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _httpContext = httpContext;
        _logger = logger;
    }

    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var log = new SystemAuditLog
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                OccurredAt = DateTime.UtcNow,
                // A background worker running a scheduled task has no signed-in user.
                UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
                Username = _currentUser.IsAuthenticated ? _currentUser.Username : "system",
                Action = entry.Action,
                Category = entry.Category,
                EntityId = entry.EntityId,
                EntityName = entry.EntityName,
                DetailsJson = entry.DetailsJson,
                IsSuccess = entry.IsSuccess,
                ErrorMessage = entry.ErrorMessage,
                // Real caller IP: UseForwardedHeaders has already resolved the proxy by now.
                IpAddress = _httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString()
            };

            await _unitOfWork.SystemAuditLogs.AddAsync(log, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Contractually non-throwing: a failed audit write must not turn a successful
            // administrative action into an error for the user. It is logged loudly instead.
            _logger.LogError(ex,
                "Could not write audit entry {Action} for {User}.", entry.Action, _currentUser.Username);
        }
    }
}
