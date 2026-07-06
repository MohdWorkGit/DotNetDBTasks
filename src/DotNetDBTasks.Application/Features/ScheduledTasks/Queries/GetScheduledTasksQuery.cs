using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.ScheduledTasks.Queries;

/// <summary>
/// Lists the scheduled tasks visible to the current user with their latest run:
/// Admins and Auditors see every task; other users see only tasks where they were
/// granted viewer permission.
/// </summary>
public record GetScheduledTasksQuery : IRequest<List<ScheduledTaskDto>>;

public class GetScheduledTasksQueryHandler : IRequestHandler<GetScheduledTasksQuery, List<ScheduledTaskDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public GetScheduledTasksQueryHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<List<ScheduledTaskDto>> Handle(GetScheduledTasksQuery request, CancellationToken cancellationToken)
    {
        var seesAll = _currentUser.Roles.Contains("Admin") || _currentUser.Roles.Contains("Auditor");

        var tasks = seesAll
            ? await _unitOfWork.ScheduledTasks.GetAllAsync(
                cancellationToken, "Items", "Items.DynamicQuery", "Viewers", "Viewers.User")
            : await _unitOfWork.ScheduledTasks.FindAsync(
                t => t.Viewers.Any(v => v.UserId == _currentUser.UserId),
                cancellationToken, "Items", "Items.DynamicQuery", "Viewers", "Viewers.User");

        var taskIds = tasks.Select(t => t.Id).ToHashSet();
        var runs = taskIds.Count == 0
            ? Array.Empty<Domain.Entities.ScheduledTaskRun>()
            : await _unitOfWork.ScheduledTaskRuns.FindAsync(
                r => taskIds.Contains(r.ScheduledTaskId), cancellationToken);
        var lastRuns = runs
            .GroupBy(r => r.ScheduledTaskId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.StartedAt).First());

        return tasks
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => ScheduledTaskMapper.ToDto(t, lastRuns.GetValueOrDefault(t.Id)))
            .ToList();
    }
}
