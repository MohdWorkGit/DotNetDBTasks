using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.ScheduledTasks.Commands;

/// <summary>
/// The three ways a scheduled task's status visibility is granted, each saved
/// independently by its own tab on the access page.
///
/// <para>
/// Every one replaces that tab's grants wholesale: what the page sends is the complete
/// list for that principal type, and the other two are left alone. Downloading is the
/// second, narrower right — an id sent as a downloader but not as a viewer is dropped,
/// since downloading a file you cannot see the run for is not a state the UI can reach
/// and not one worth storing.
/// </para>
/// </summary>
internal static class ScheduledTaskAccessAssignment
{
    /// <summary>
    /// Confirms the task exists before any grant is written; the id comes from a route,
    /// so a stale link would otherwise silently write orphan rows.
    /// </summary>
    internal static async Task<ScheduledTask> RequireTaskAsync(
        IUnitOfWork unitOfWork, Guid taskId, CancellationToken cancellationToken) =>
        await unitOfWork.ScheduledTasks.GetByIdAsync(taskId, cancellationToken)
        ?? throw new NotFoundException(nameof(ScheduledTask), taskId);
}

/// <summary>Replaces the roles granted viewer access to a scheduled task.</summary>
public class AssignScheduledTaskRolesCommand : IRequest<Unit>
{
    public Guid TaskId { get; set; }
    public List<Guid> RoleIds { get; set; } = new();

    /// <summary>Roles whose holders may also download the run's export files. Ids not
    /// present in <see cref="RoleIds"/> are ignored (download implies view).</summary>
    public List<Guid> DownloadRoleIds { get; set; } = new();
}

public class AssignScheduledTaskRolesCommandHandler
    : IRequestHandler<AssignScheduledTaskRolesCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignScheduledTaskRolesCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(AssignScheduledTaskRolesCommand request, CancellationToken cancellationToken)
    {
        await ScheduledTaskAccessAssignment.RequireTaskAsync(_unitOfWork, request.TaskId, cancellationToken);

        var roleIds = request.RoleIds.Distinct().ToList();
        foreach (var roleId in roleIds)
        {
            if (!await _unitOfWork.Roles.ExistsAsync(r => r.Id == roleId, cancellationToken))
                throw new DomainException("One of the selected roles no longer exists.");
        }

        var existing = await _unitOfWork.ScheduledTaskViewerRoles.FindAsync(
            r => r.ScheduledTaskId == request.TaskId, cancellationToken);
        foreach (var grant in existing)
            _unitOfWork.ScheduledTaskViewerRoles.Delete(grant);

        var downloadRoleIds = request.DownloadRoleIds.ToHashSet();
        foreach (var roleId in roleIds)
        {
            await _unitOfWork.ScheduledTaskViewerRoles.AddAsync(new ScheduledTaskViewerRole
            {
                ScheduledTaskId = request.TaskId,
                RoleId = roleId,
                CanDownloadFiles = downloadRoleIds.Contains(roleId)
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Replaces the user groups granted viewer access to a scheduled task.</summary>
public class AssignScheduledTaskUserGroupsCommand : IRequest<Unit>
{
    public Guid TaskId { get; set; }
    public List<Guid> UserGroupIds { get; set; } = new();

    /// <summary>Groups whose members may also download the run's export files. Ids not
    /// present in <see cref="UserGroupIds"/> are ignored (download implies view).</summary>
    public List<Guid> DownloadUserGroupIds { get; set; } = new();
}

public class AssignScheduledTaskUserGroupsCommandHandler
    : IRequestHandler<AssignScheduledTaskUserGroupsCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignScheduledTaskUserGroupsCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(AssignScheduledTaskUserGroupsCommand request, CancellationToken cancellationToken)
    {
        await ScheduledTaskAccessAssignment.RequireTaskAsync(_unitOfWork, request.TaskId, cancellationToken);

        var groupIds = request.UserGroupIds.Distinct().ToList();
        foreach (var groupId in groupIds)
        {
            if (!await _unitOfWork.UserGroups.ExistsAsync(g => g.Id == groupId, cancellationToken))
                throw new DomainException("One of the selected user groups no longer exists.");
        }

        var existing = await _unitOfWork.ScheduledTaskViewerUserGroups.FindAsync(
            g => g.ScheduledTaskId == request.TaskId, cancellationToken);
        foreach (var grant in existing)
            _unitOfWork.ScheduledTaskViewerUserGroups.Delete(grant);

        var downloadGroupIds = request.DownloadUserGroupIds.ToHashSet();
        foreach (var groupId in groupIds)
        {
            await _unitOfWork.ScheduledTaskViewerUserGroups.AddAsync(new ScheduledTaskViewerUserGroup
            {
                ScheduledTaskId = request.TaskId,
                UserGroupId = groupId,
                CanDownloadFiles = downloadGroupIds.Contains(groupId)
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Replaces the individually named users granted viewer access to a scheduled task.</summary>
public class AssignScheduledTaskUsersCommand : IRequest<Unit>
{
    public Guid TaskId { get; set; }
    public List<Guid> UserIds { get; set; } = new();

    /// <summary>Users who may also download the run's export files. Ids not present in
    /// <see cref="UserIds"/> are ignored (download implies view).</summary>
    public List<Guid> DownloadUserIds { get; set; } = new();
}

public class AssignScheduledTaskUsersCommandHandler
    : IRequestHandler<AssignScheduledTaskUsersCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignScheduledTaskUsersCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(AssignScheduledTaskUsersCommand request, CancellationToken cancellationToken)
    {
        await ScheduledTaskAccessAssignment.RequireTaskAsync(_unitOfWork, request.TaskId, cancellationToken);

        var userIds = request.UserIds.Distinct().ToList();
        foreach (var userId in userIds)
        {
            if (!await _unitOfWork.Users.ExistsAsync(u => u.Id == userId, cancellationToken))
                throw new DomainException("One of the selected viewer users no longer exists.");
        }

        var existing = await _unitOfWork.ScheduledTaskViewers.FindAsync(
            v => v.ScheduledTaskId == request.TaskId, cancellationToken);
        foreach (var grant in existing)
            _unitOfWork.ScheduledTaskViewers.Delete(grant);

        var downloadUserIds = request.DownloadUserIds.ToHashSet();
        foreach (var userId in userIds)
        {
            await _unitOfWork.ScheduledTaskViewers.AddAsync(new ScheduledTaskViewer
            {
                ScheduledTaskId = request.TaskId,
                UserId = userId,
                CanDownloadFiles = downloadUserIds.Contains(userId)
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
