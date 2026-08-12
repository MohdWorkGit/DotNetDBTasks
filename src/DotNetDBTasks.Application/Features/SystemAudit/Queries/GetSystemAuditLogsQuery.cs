using System.Linq.Expressions;
using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.SystemAudit.Queries;

public class SystemAuditLogDto
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>Stable code (<c>users.create</c>); the client renders it in the active language.</summary>
    public string Action { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public string? DetailsJson { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>
/// One page of the administrative audit trail, newest first. Admin and Auditor only —
/// see <c>SystemAuditLogsController</c>.
/// </summary>
public class GetSystemAuditLogsQuery : IRequest<PaginatedList<SystemAuditLogDto>>
{
    public string? Category { get; set; }
    public string? Action { get; set; }
    public Guid? UserId { get; set; }
    public bool? IsSuccess { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }

    /// <summary>Matches username, entity name or the details payload.</summary>
    public string? Search { get; set; }

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class GetSystemAuditLogsQueryHandler
    : IRequestHandler<GetSystemAuditLogsQuery, PaginatedList<SystemAuditLogDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetSystemAuditLogsQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PaginatedList<SystemAuditLogDto>> Handle(
        GetSystemAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var pageNumber = Math.Max(request.PageNumber, 1);

        // Filtering, ordering and projection all run in the database via GetPagedAsync. This
        // table only grows, so materializing it would get slower every day the system runs.
        var (items, totalCount) = await _unitOfWork.SystemAuditLogs.GetPagedAsync(
            BuildFilter(request),
            l => l.OccurredAt,
            descending: true,
            pageNumber,
            pageSize,
            l => new SystemAuditLogDto
            {
                Id = l.Id,
                OccurredAt = l.OccurredAt,
                Username = l.Username,
                Action = l.Action,
                Category = l.Category,
                EntityName = l.EntityName,
                DetailsJson = l.DetailsJson,
                IsSuccess = l.IsSuccess,
                ErrorMessage = l.ErrorMessage,
                IpAddress = l.IpAddress
            },
            cancellationToken);

        return new PaginatedList<SystemAuditLogDto>(items, totalCount, pageNumber, pageSize);
    }

    /// <summary>
    /// Composes the active filters into a single predicate. Built as one expression rather
    /// than chained Where calls because <see cref="IRepository{T}.GetPagedAsync"/> takes one.
    /// </summary>
    private static Expression<Func<SystemAuditLog, bool>>? BuildFilter(GetSystemAuditLogsQuery r)
    {
        var category = string.IsNullOrWhiteSpace(r.Category) ? null : r.Category;
        var action = string.IsNullOrWhiteSpace(r.Action) ? null : r.Action;
        var term = string.IsNullOrWhiteSpace(r.Search) ? null : r.Search.Trim().ToLower();

        if (category is null && action is null && term is null
            && r.UserId is null && r.IsSuccess is null && r.FromUtc is null && r.ToUtc is null)
        {
            return null;
        }

        return l =>
            (category == null || l.Category == category) &&
            (action == null || l.Action == action) &&
            (!r.UserId.HasValue || l.UserId == r.UserId.Value) &&
            (!r.IsSuccess.HasValue || l.IsSuccess == r.IsSuccess.Value) &&
            (!r.FromUtc.HasValue || l.OccurredAt >= r.FromUtc.Value) &&
            (!r.ToUtc.HasValue || l.OccurredAt <= r.ToUtc.Value) &&
            (term == null ||
                l.Username.ToLower().Contains(term) ||
                (l.EntityName != null && l.EntityName.ToLower().Contains(term)) ||
                (l.DetailsJson != null && l.DetailsJson.ToLower().Contains(term)));
    }
}
