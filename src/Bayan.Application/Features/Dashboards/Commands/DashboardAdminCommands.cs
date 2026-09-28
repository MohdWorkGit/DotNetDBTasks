using Bayan.Application.Common.Interfaces;
using Bayan.Application.Features.Dashboards.Dtos;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Dashboards.Commands;

/// <summary>
/// Deletes a dashboard. Its tiles, filters, maps and grants go with it by cascade; the queries
/// its tiles read are untouched.
/// </summary>
public class DeleteDashboardCommand : IRequest<Unit>
{
    public Guid Id { get; set; }
}

public class DeleteDashboardCommandHandler : IRequestHandler<DeleteDashboardCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDashboardTileCache _cache;

    public DeleteDashboardCommandHandler(IUnitOfWork unitOfWork, IDashboardTileCache cache)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<Unit> Handle(DeleteDashboardCommand request, CancellationToken cancellationToken)
    {
        var dashboard = await _unitOfWork.Dashboards.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.Id);

        // Said plainly here rather than left to the foreign key, which would surface as a
        // database error naming a constraint.
        var usedBy = await _unitOfWork.ScheduledTaskItems.FindAsync(
            i => i.DashboardId == request.Id, cancellationToken, "ScheduledTask");
        if (usedBy.Count > 0)
        {
            var tasks = string.Join(", ", usedBy.Select(i => i.ScheduledTask.Name).Distinct());
            throw new DomainException(
                $"This dashboard is snapshotted by the scheduled task(s) {tasks}. Remove it from them first.");
        }

        _unitOfWork.Dashboards.Delete(dashboard);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _cache.InvalidateDashboard(request.Id);
        return Unit.Value;
    }
}

// ----------------------------------------------------------------------------

/// <summary>
/// Replaces who a dashboard is granted to, as one set — see <c>SetReportAccessCommand</c>.
///
/// <para>The tile cache needs no invalidation here: it holds results, not decisions, and every
/// viewer's grant is checked on every request before the cache is consulted.</para>
/// </summary>
public class SetDashboardAccessCommand : IRequest<DashboardAccessDto>
{
    public Guid DashboardId { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
    public List<Guid> UserGroupIds { get; set; } = new();
    public List<Guid> UserIds { get; set; } = new();
}

public class SetDashboardAccessCommandHandler : IRequestHandler<SetDashboardAccessCommand, DashboardAccessDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public SetDashboardAccessCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<DashboardAccessDto> Handle(
        SetDashboardAccessCommand request,
        CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.Dashboards.ExistsAsync(d => d.Id == request.DashboardId, cancellationToken);
        if (!exists)
            throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        foreach (var grant in await _unitOfWork.DashboardRoles.FindAsync(
            r => r.DashboardId == request.DashboardId, cancellationToken))
            _unitOfWork.DashboardRoles.Delete(grant);

        foreach (var grant in await _unitOfWork.DashboardUserGroups.FindAsync(
            g => g.DashboardId == request.DashboardId, cancellationToken))
            _unitOfWork.DashboardUserGroups.Delete(grant);

        foreach (var grant in await _unitOfWork.DashboardUsers.FindAsync(
            u => u.DashboardId == request.DashboardId, cancellationToken))
            _unitOfWork.DashboardUsers.Delete(grant);

        foreach (var roleId in request.RoleIds.Distinct())
        {
            await _unitOfWork.DashboardRoles.AddAsync(
                new DashboardRole { DashboardId = request.DashboardId, RoleId = roleId }, cancellationToken);
        }

        foreach (var groupId in request.UserGroupIds.Distinct())
        {
            await _unitOfWork.DashboardUserGroups.AddAsync(
                new DashboardUserGroup { DashboardId = request.DashboardId, UserGroupId = groupId }, cancellationToken);
        }

        foreach (var userId in request.UserIds.Distinct())
        {
            await _unitOfWork.DashboardUsers.AddAsync(
                new DashboardUser { DashboardId = request.DashboardId, UserId = userId }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DashboardAccessDto
        {
            RoleIds = request.RoleIds.Distinct().ToList(),
            UserGroupIds = request.UserGroupIds.Distinct().ToList(),
            UserIds = request.UserIds.Distinct().ToList()
        };
    }
}
