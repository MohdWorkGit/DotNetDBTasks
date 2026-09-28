using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Application.Features.Dashboards.Dtos;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Dashboards.Queries;

// ---------------------------------------------------------------- admin list

/// <summary>Every dashboard, for the admin list.</summary>
public class GetDashboardsQuery : IRequest<List<DashboardSummaryDto>>;

public class GetDashboardsQueryHandler : IRequestHandler<GetDashboardsQuery, List<DashboardSummaryDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetDashboardsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<List<DashboardSummaryDto>> Handle(GetDashboardsQuery request, CancellationToken cancellationToken)
    {
        var dashboards = await _unitOfWork.Dashboards.GetAllAsync(cancellationToken, "Tiles", "Filters");

        return dashboards
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Select(DashboardMapper.ToSummary)
            .ToList();
    }
}

// ---------------------------------------------------------------- admin detail

public class GetDashboardByIdQuery : IRequest<DashboardDto>
{
    public Guid Id { get; set; }
}

public class GetDashboardByIdQueryHandler : IRequestHandler<GetDashboardByIdQuery, DashboardDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetDashboardByIdQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public Task<DashboardDto> Handle(GetDashboardByIdQuery request, CancellationToken cancellationToken) =>
        DashboardMapper.LoadDtoAsync(_unitOfWork, request.Id, cancellationToken);
}

// ---------------------------------------------------------------- user list

/// <summary>The dashboards this caller may open. Disabled ones are left out.</summary>
public class GetMyDashboardsQuery : IRequest<List<DashboardSummaryDto>>;

public class GetMyDashboardsQueryHandler : IRequestHandler<GetMyDashboardsQuery, List<DashboardSummaryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public GetMyDashboardsQueryHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<List<DashboardSummaryDto>> Handle(GetMyDashboardsQuery request, CancellationToken cancellationToken)
    {
        var reachable = await DashboardAccess.ReachableIdsAsync(_unitOfWork, _currentUser, cancellationToken);
        var dashboards = await _unitOfWork.Dashboards.FindAsync(
            d => d.IsEnabled, cancellationToken, "Tiles", "Filters");

        return dashboards
            .Where(d => reachable is null || reachable.Contains(d.Id))
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Select(DashboardMapper.ToSummary)
            .ToList();
    }
}

// ---------------------------------------------------------------- user detail

/// <summary>
/// One dashboard's layout for the viewer. Access is checked here, not only per tile, so the page
/// is never laid out for a dashboard the caller could not then fill.
/// </summary>
public class GetMyDashboardByIdQuery : IRequest<DashboardDto>
{
    public Guid Id { get; set; }
}

public class GetMyDashboardByIdQueryHandler : IRequestHandler<GetMyDashboardByIdQuery, DashboardDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public GetMyDashboardByIdQueryHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<DashboardDto> Handle(GetMyDashboardByIdQuery request, CancellationToken cancellationToken)
    {
        await DashboardAccess.EnsureCanViewAsync(request.Id, _unitOfWork, _currentUser, cancellationToken);

        var dashboard = await _unitOfWork.Dashboards.GetByIdAsync(
            request.Id, cancellationToken, DashboardMapper.FullIncludes)
            ?? throw new NotFoundException(nameof(Dashboard), request.Id);

        if (!dashboard.IsEnabled)
            throw new DomainException("This dashboard is currently disabled.");

        return DashboardMapper.ToDto(dashboard);
    }
}

// ---------------------------------------------------------------- access

public class GetDashboardAccessQuery : IRequest<DashboardAccessDto>
{
    public Guid Id { get; set; }
}

public class GetDashboardAccessQueryHandler : IRequestHandler<GetDashboardAccessQuery, DashboardAccessDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetDashboardAccessQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<DashboardAccessDto> Handle(GetDashboardAccessQuery request, CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.Dashboards.ExistsAsync(d => d.Id == request.Id, cancellationToken);
        if (!exists)
            throw new NotFoundException(nameof(Dashboard), request.Id);

        var roles = await _unitOfWork.DashboardRoles.FindAsync(r => r.DashboardId == request.Id, cancellationToken);
        var groups = await _unitOfWork.DashboardUserGroups.FindAsync(g => g.DashboardId == request.Id, cancellationToken);
        var users = await _unitOfWork.DashboardUsers.FindAsync(u => u.DashboardId == request.Id, cancellationToken);

        return new DashboardAccessDto
        {
            RoleIds = roles.Select(r => r.RoleId).ToList(),
            UserGroupIds = groups.Select(g => g.UserGroupId).ToList(),
            UserIds = users.Select(u => u.UserId).ToList()
        };
    }
}
