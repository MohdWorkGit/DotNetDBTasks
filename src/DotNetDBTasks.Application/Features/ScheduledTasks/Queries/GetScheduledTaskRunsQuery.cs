using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.ScheduledTasks.Queries;

/// <summary>
/// Returns a scheduled task's run history, newest first. Same visibility rule as
/// the task itself (Admin/Auditor, or explicit viewer grant).
/// </summary>
public record GetScheduledTaskRunsQuery(Guid TaskId, int Take = 50) : IRequest<List<ScheduledTaskRunDto>>;

public class GetScheduledTaskRunsQueryHandler : IRequestHandler<GetScheduledTaskRunsQuery, List<ScheduledTaskRunDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public GetScheduledTaskRunsQueryHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<List<ScheduledTaskRunDto>> Handle(GetScheduledTaskRunsQuery request, CancellationToken cancellationToken)
    {
        var task = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == request.TaskId, cancellationToken, "Viewers")).FirstOrDefault()
            ?? throw new NotFoundException(nameof(ScheduledTask), request.TaskId);

        ScheduledTaskAccess.EnsureCanView(task, _currentUser);

        var take = Math.Clamp(request.Take, 1, 500);
        return (await _unitOfWork.ScheduledTaskRuns.FindAsync(
                r => r.ScheduledTaskId == task.Id, cancellationToken))
            .OrderByDescending(r => r.StartedAt)
            .Take(take)
            .Select(ScheduledTaskMapper.ToRunDto)
            .ToList();
    }
}
