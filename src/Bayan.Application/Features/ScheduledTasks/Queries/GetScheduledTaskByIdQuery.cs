using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Constants;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.ScheduledTasks.Queries;

/// <summary>
/// Fetches one scheduled task with items and viewers. Admins/Auditors always have
/// access; other users only when granted viewer permission on the task.
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
            "Triggers", "Items", "Items.DynamicQuery", "Viewers", "Viewers.User")).FirstOrDefault()
            ?? throw new NotFoundException(nameof(ScheduledTask), request.Id);

        await ScheduledTaskAccess.EnsureCanViewAsync(task, _currentUser, _permissions, cancellationToken);

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
            task, _currentUser, _permissions, cancellationToken);
        return dto;
    }
}

/// <summary>
/// Shared read-permission rule: Admins and Auditors see every scheduled task;
/// everyone else needs an explicit viewer grant.
///
/// <para>
/// Downloading a run's export files is a narrower right than viewing. An Auditor reads
/// the task's configuration and run history, but the exports themselves are query results —
/// the data the Auditor role is deliberately not given. So downloading needs Admin, or an
/// explicit viewer grant carrying <c>CanDownloadFiles</c>; an Auditor named as a viewer with
/// that grant may download, like any other user.
/// </para>
/// </summary>
public static class ScheduledTaskAccess
{
    public static async Task EnsureCanViewAsync(
        ScheduledTask task,
        ICurrentUserService currentUser,
        IPermissionService permissions,
        CancellationToken cancellationToken = default)
    {
        var seesAll = await permissions.HasAsync(
            currentUser.Roles, Permissions.ScheduledTasksViewAll, cancellationToken);

        if (!seesAll && task.Viewers.All(v => v.UserId != currentUser.UserId))
            throw new ForbiddenAccessException("You do not have access to this scheduled task.");
    }

    /// <summary>
    /// Requires the task's Viewers navigation to be loaded. Two ways in: the blanket
    /// <c>scheduledTasks.download</c> permission, or a viewer grant on this task that allows
    /// downloads — the per-task grant is what lets one person have the files for one task
    /// without being given every task's.
    /// </summary>
    public static async Task<bool> CanDownloadFilesAsync(
        ScheduledTask task,
        ICurrentUserService currentUser,
        IPermissionService permissions,
        CancellationToken cancellationToken = default) =>
        await permissions.HasAsync(currentUser.Roles, Permissions.ScheduledTasksDownload, cancellationToken)
        || task.Viewers.Any(v => v.UserId == currentUser.UserId && v.CanDownloadFiles);

    public static async Task EnsureCanDownloadFilesAsync(
        ScheduledTask task,
        ICurrentUserService currentUser,
        IPermissionService permissions,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanViewAsync(task, currentUser, permissions, cancellationToken);

        if (!await CanDownloadFilesAsync(task, currentUser, permissions, cancellationToken))
            throw new ForbiddenAccessException("You do not have permission to download this task's files.");
    }
}
