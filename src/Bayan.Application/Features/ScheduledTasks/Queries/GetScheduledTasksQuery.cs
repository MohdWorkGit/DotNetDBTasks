using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Domain.Constants;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.ScheduledTasks.Queries;

/// <summary>
/// Lists the scheduled tasks visible to the current user with their latest run. Holding
/// <c>scheduledTasks.viewAll</c> shows every task; without it a user sees the tasks a viewer
/// grant reaches them on — through one of their roles, a user group they belong to, or their
/// own name.
/// </summary>
public record GetScheduledTasksQuery : IRequest<List<ScheduledTaskDto>>;

public class GetScheduledTasksQueryHandler : IRequestHandler<GetScheduledTasksQuery, List<ScheduledTaskDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;

    public GetScheduledTasksQueryHandler(
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, IPermissionService permissions)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _permissions = permissions;
    }

    /// <summary>
    /// The newest run per task, in two queries that never touch a CLOB they will not use.
    ///
    /// <para>This used to be one <c>FindAsync</c> over every run of every visible task, kept
    /// only to pick the latest of each. Each row drags <c>ItemResultsJson</c> — a CLOB — back
    /// with it, so a few hundred rows of history turned a 6 KB response into a 19-second one.
    /// Now a projection finds which runs are wanted (no CLOB, straight off the ScheduledTaskId
    /// index), and only those handful of rows are fetched whole.</para>
    /// </summary>
    private async Task<Dictionary<Guid, Domain.Entities.ScheduledTaskRun>> LoadLastRunsAsync(
        HashSet<Guid> taskIds, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, Domain.Entities.ScheduledTaskRun>();

        var (summaries, _) = await _unitOfWork.ScheduledTaskRuns.GetPagedAsync(
            r => taskIds.Contains(r.ScheduledTaskId),
            r => r.StartedAt,
            descending: true,
            pageNumber: 1,
            pageSize: int.MaxValue,
            r => new { r.Id, r.ScheduledTaskId, r.StartedAt },
            cancellationToken);

        var newestIds = summaries
            .GroupBy(r => r.ScheduledTaskId)
            .Select(g => g.OrderByDescending(r => r.StartedAt).First().Id)
            .ToHashSet();

        if (newestIds.Count == 0)
            return new Dictionary<Guid, Domain.Entities.ScheduledTaskRun>();

        var runs = await _unitOfWork.ScheduledTaskRuns.FindAsync(
            r => newestIds.Contains(r.Id), cancellationToken);

        return runs.ToDictionary(r => r.ScheduledTaskId);
    }

    public async Task<List<ScheduledTaskDto>> Handle(GetScheduledTasksQuery request, CancellationToken cancellationToken)
    {
        var seesAll = await _permissions.HasAsync(
            _currentUser.Roles, Permissions.ScheduledTasksViewAll, cancellationToken);

        // Resolved once for the whole list; every task below is then filtered in memory
        // against the same role and group ids.
        var principal = await ScheduledTaskPrincipal.ResolveAsync(
            _unitOfWork, _currentUser.UserId, cancellationToken);

        var includes = ScheduledTaskAccess.GrantIncludes
            .Concat(new[] { "Triggers", "Items", "Items.DynamicQuery" })
            .ToArray();

        var tasks = seesAll
            ? await _unitOfWork.ScheduledTasks.GetAllAsync(cancellationToken, includes)
            : await _unitOfWork.ScheduledTasks.FindAsync(
                t => t.Viewers.Any(v => v.UserId == principal.UserId)
                    || t.ViewerRoles.Any(r => principal.RoleIds.Contains(r.RoleId))
                    || t.ViewerUserGroups.Any(g => principal.UserGroupIds.Contains(g.UserGroupId)),
                cancellationToken, includes);

        var taskIds = tasks.Select(t => t.Id).ToHashSet();
        var lastRuns = await LoadLastRunsAsync(taskIds, cancellationToken);

        var canDownloadAny = await _permissions.HasAsync(
            _currentUser.Roles, Permissions.ScheduledTasksDownload, cancellationToken);

        return tasks
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t =>
            {
                var dto = ScheduledTaskMapper.ToDto(t, lastRuns.GetValueOrDefault(t.Id));
                dto.CanDownloadFiles = canDownloadAny
                    || ScheduledTaskAccess.HasDownloadGrant(t, principal);
                return dto;
            })
            .ToList();
    }
}
