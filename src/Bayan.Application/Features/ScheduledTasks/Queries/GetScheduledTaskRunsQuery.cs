using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.ScheduledTasks.Queries;

/// <summary>
/// Returns a scheduled task's run history, newest first. Same visibility rule as
/// the task itself (<c>scheduledTasks.viewAll</c>, or a viewer grant by role,
/// user group, or name).
/// </summary>
public record GetScheduledTaskRunsQuery(Guid TaskId, int Take = 50) : IRequest<List<ScheduledTaskRunDto>>;

public class GetScheduledTaskRunsQueryHandler : IRequestHandler<GetScheduledTaskRunsQuery, List<ScheduledTaskRunDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;

    public GetScheduledTaskRunsQueryHandler(
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, IPermissionService permissions)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _permissions = permissions;
    }

    public async Task<List<ScheduledTaskRunDto>> Handle(GetScheduledTaskRunsQuery request, CancellationToken cancellationToken)
    {
        var task = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == request.TaskId, cancellationToken,
            ScheduledTaskAccess.GrantIncludes)).FirstOrDefault()
            ?? throw new NotFoundException(nameof(ScheduledTask), request.TaskId);

        await ScheduledTaskAccess.EnsureCanViewAsync(
            task, _unitOfWork, _currentUser, _permissions, cancellationToken);

        // Sorted and cut off by Oracle. Sorting in memory meant materializing the task's whole
        // history — CLOBs and all — to show the most recent page of it.
        var take = Math.Clamp(request.Take, 1, 500);
        var (runs, _) = await _unitOfWork.ScheduledTaskRuns.GetPagedAsync(
            r => r.ScheduledTaskId == task.Id,
            r => r.StartedAt,
            descending: true,
            pageNumber: 1,
            pageSize: take,
            r => r,
            cancellationToken);

        return runs.Select(ScheduledTaskMapper.ToRunDto).ToList();
    }
}
