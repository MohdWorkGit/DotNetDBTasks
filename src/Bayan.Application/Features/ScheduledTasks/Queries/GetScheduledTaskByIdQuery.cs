using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Domain.Constants;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.ScheduledTasks.Queries;

/// <summary>
/// Fetches one scheduled task with its items and access grants. Holders of
/// <c>scheduledTasks.viewAll</c> always have access; other users only when a viewer
/// grant reaches them — by role, by user group, or by name.
/// </summary>
public record GetScheduledTaskByIdQuery(Guid Id) : IRequest<ScheduledTaskDto>;

public class GetScheduledTaskByIdQueryHandler : IRequestHandler<GetScheduledTaskByIdQuery, ScheduledTaskDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;

    public GetScheduledTaskByIdQueryHandler(
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, IPermissionService permissions)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _permissions = permissions;
    }

    public async Task<ScheduledTaskDto> Handle(GetScheduledTaskByIdQuery request, CancellationToken cancellationToken)
    {
        var task = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == request.Id, cancellationToken,
            ScheduledTaskAccess.GrantIncludes
                .Concat(new[] { "Triggers", "Items", "Items.DynamicQuery" }).ToArray())).FirstOrDefault()
            ?? throw new NotFoundException(nameof(ScheduledTask), request.Id);

        await ScheduledTaskAccess.EnsureCanViewAsync(
            task, _unitOfWork, _currentUser, _permissions, cancellationToken);

        // Ordered and limited in the database. Reading every run to keep the newest one meant
        // pulling every ItemResultsJson CLOB with it.
        var (newest, _) = await _unitOfWork.ScheduledTaskRuns.GetPagedAsync(
            r => r.ScheduledTaskId == task.Id,
            r => r.StartedAt,
            descending: true,
            pageNumber: 1,
            pageSize: 1,
            r => r,
            cancellationToken);
        var lastRun = newest.FirstOrDefault();

        var dto = ScheduledTaskMapper.ToDto(task, lastRun);
        dto.CanDownloadFiles = await ScheduledTaskAccess.CanDownloadFilesAsync(
            task, _unitOfWork, _currentUser, _permissions, cancellationToken);
        return dto;
    }
}
