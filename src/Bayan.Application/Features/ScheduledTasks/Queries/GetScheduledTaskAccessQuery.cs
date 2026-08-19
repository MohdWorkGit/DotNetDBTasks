using Bayan.Application.Common.Security;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.ScheduledTasks.Queries;

/// <summary>
/// Everything the "Manage Task Access" page renders: the task's name plus its three
/// lists of viewer grants, each flagged with whether that grant also allows downloads.
/// </summary>
public class ScheduledTaskAccessDto
{
    public Guid TaskId { get; set; }
    public string TaskName { get; set; } = string.Empty;

    public List<ScheduledTaskRoleGrantDto> Roles { get; set; } = new();
    public List<ScheduledTaskUserGroupGrantDto> UserGroups { get; set; } = new();
    public List<ScheduledTaskViewerDto> Users { get; set; } = new();
}

public class ScheduledTaskRoleGrantDto
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;

    /// <summary>When true the role's holders may also download the run's export files.</summary>
    public bool CanDownloadFiles { get; set; }
}

public class ScheduledTaskUserGroupGrantDto
{
    public Guid UserGroupId { get; set; }
    public string UserGroupName { get; set; } = string.Empty;

    /// <summary>When true the group's members may also download the run's export files.</summary>
    public bool CanDownloadFiles { get; set; }
}

/// <summary>
/// Reads one task's viewer grants for the access page. Guarded by
/// <c>scheduledTasks.manage</c> at the controller — who may see a task is not itself
/// something a viewer of that task gets to read.
/// </summary>
public record GetScheduledTaskAccessQuery(Guid TaskId) : IRequest<ScheduledTaskAccessDto>;

public class GetScheduledTaskAccessQueryHandler
    : IRequestHandler<GetScheduledTaskAccessQuery, ScheduledTaskAccessDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetScheduledTaskAccessQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ScheduledTaskAccessDto> Handle(
        GetScheduledTaskAccessQuery request, CancellationToken cancellationToken)
    {
        var task = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == request.TaskId, cancellationToken,
            ScheduledTaskAccess.GrantIncludes)).FirstOrDefault()
            ?? throw new NotFoundException(nameof(ScheduledTask), request.TaskId);

        return new ScheduledTaskAccessDto
        {
            TaskId = task.Id,
            TaskName = task.Name,
            Roles = task.ViewerRoles
                .Select(r => new ScheduledTaskRoleGrantDto
                {
                    RoleId = r.RoleId,
                    RoleName = r.Role?.Name ?? string.Empty,
                    CanDownloadFiles = r.CanDownloadFiles
                })
                .OrderBy(r => r.RoleName)
                .ToList(),
            UserGroups = task.ViewerUserGroups
                .Select(g => new ScheduledTaskUserGroupGrantDto
                {
                    UserGroupId = g.UserGroupId,
                    UserGroupName = g.UserGroup?.Name ?? string.Empty,
                    CanDownloadFiles = g.CanDownloadFiles
                })
                .OrderBy(g => g.UserGroupName)
                .ToList(),
            Users = task.Viewers
                .Select(v => new ScheduledTaskViewerDto
                {
                    UserId = v.UserId,
                    Username = v.User?.Username ?? string.Empty,
                    CanDownloadFiles = v.CanDownloadFiles
                })
                .OrderBy(v => v.Username)
                .ToList()
        };
    }
}
