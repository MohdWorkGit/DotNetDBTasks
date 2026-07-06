using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.ScheduledTasks.Queries;

/// <summary>
/// Fetches one scheduled task with items and viewers. Admins/Auditors always have
/// access; other users only when granted viewer permission on the task.
/// </summary>
public record GetScheduledTaskByIdQuery(Guid Id) : IRequest<ScheduledTaskDto>;

public class GetScheduledTaskByIdQueryHandler : IRequestHandler<GetScheduledTaskByIdQuery, ScheduledTaskDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public GetScheduledTaskByIdQueryHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<ScheduledTaskDto> Handle(GetScheduledTaskByIdQuery request, CancellationToken cancellationToken)
    {
        var task = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == request.Id, cancellationToken,
            "Items", "Items.DynamicQuery", "Viewers", "Viewers.User")).FirstOrDefault()
            ?? throw new NotFoundException(nameof(ScheduledTask), request.Id);

        ScheduledTaskAccess.EnsureCanView(task, _currentUser);

        var lastRun = (await _unitOfWork.ScheduledTaskRuns.FindAsync(
                r => r.ScheduledTaskId == task.Id, cancellationToken))
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefault();

        return ScheduledTaskMapper.ToDto(task, lastRun);
    }
}

/// <summary>
/// Shared read-permission rule: Admins and Auditors see every scheduled task;
/// everyone else needs an explicit viewer grant.
/// </summary>
public static class ScheduledTaskAccess
{
    public static void EnsureCanView(ScheduledTask task, ICurrentUserService currentUser)
    {
        var seesAll = currentUser.Roles.Contains("Admin") || currentUser.Roles.Contains("Auditor");
        if (!seesAll && task.Viewers.All(v => v.UserId != currentUser.UserId))
            throw new ForbiddenAccessException("You do not have access to this scheduled task.");
    }
}
