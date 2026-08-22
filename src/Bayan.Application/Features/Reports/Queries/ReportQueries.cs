using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Application.Features.Reports.Dtos;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Reports.Queries;

// ---------------------------------------------------------------- admin list

/// <summary>Every report, for the admin list.</summary>
public class GetReportsQuery : IRequest<List<ReportSummaryDto>>;

public class GetReportsQueryHandler : IRequestHandler<GetReportsQuery, List<ReportSummaryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUserService _currentUser;

    public GetReportsQueryHandler(
        IUnitOfWork unitOfWork, IPermissionService permissions, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _permissions = permissions;
        _currentUser = currentUser;
    }

    public async Task<List<ReportSummaryDto>> Handle(GetReportsQuery request, CancellationToken cancellationToken)
    {
        var held = await _permissions.GetForRolesAsync(_currentUser.Roles, cancellationToken);
        var reports = await _unitOfWork.Reports.GetAllAsync(cancellationToken, "Datasets", "Parameters");

        return reports
            .OrderBy(r => r.Name)
            .Select(r => ReportMapper.ToSummary(r, held))
            .ToList();
    }
}

// ---------------------------------------------------------------- admin detail

/// <summary>One report's full definition, for the builder.</summary>
public class GetReportByIdQuery : IRequest<ReportDto>
{
    public Guid Id { get; set; }
}

public class GetReportByIdQueryHandler : IRequestHandler<GetReportByIdQuery, ReportDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUserService _currentUser;

    public GetReportByIdQueryHandler(
        IUnitOfWork unitOfWork, IPermissionService permissions, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _permissions = permissions;
        _currentUser = currentUser;
    }

    public async Task<ReportDto> Handle(GetReportByIdQuery request, CancellationToken cancellationToken)
    {
        var held = await _permissions.GetForRolesAsync(_currentUser.Roles, cancellationToken);
        return await ReportMapper.LoadDtoAsync(_unitOfWork, request.Id, cancellationToken, held);
    }
}

// ---------------------------------------------------------------- user list

/// <summary>
/// The reports this caller may run — what "My Reports" shows. Disabled reports are left out:
/// a report nobody can run has no place on a list of things to run.
/// </summary>
public class GetMyReportsQuery : IRequest<List<ReportSummaryDto>>;

public class GetMyReportsQueryHandler : IRequestHandler<GetMyReportsQuery, List<ReportSummaryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUserService _currentUser;

    public GetMyReportsQueryHandler(
        IUnitOfWork unitOfWork, IPermissionService permissions, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _permissions = permissions;
        _currentUser = currentUser;
    }

    public async Task<List<ReportSummaryDto>> Handle(GetMyReportsQuery request, CancellationToken cancellationToken)
    {
        var held = await _permissions.GetForRolesAsync(_currentUser.Roles, cancellationToken);
        var reach = await ReportAccess.ResolveAsync(_unitOfWork, _currentUser, cancellationToken);

        var reports = await _unitOfWork.Reports.FindAsync(
            r => r.IsEnabled, cancellationToken, "Datasets", "Parameters", "QueryGroup");

        return reports
            .Where(r => ReportAccess.Reaches(reach, r))
            .OrderBy(r => r.Name)
            .Select(r => ReportMapper.ToSummary(r, held))
            .ToList();
    }
}

// ---------------------------------------------------------------- user detail

/// <summary>
/// One report's definition for the run form. Access is checked here rather than only at run
/// time, so the form is never rendered for a report the caller could not then run.
/// </summary>
public class GetMyReportByIdQuery : IRequest<ReportDto>
{
    public Guid Id { get; set; }
}

public class GetMyReportByIdQueryHandler : IRequestHandler<GetMyReportByIdQuery, ReportDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUserService _currentUser;

    public GetMyReportByIdQueryHandler(
        IUnitOfWork unitOfWork, IPermissionService permissions, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _permissions = permissions;
        _currentUser = currentUser;
    }

    public async Task<ReportDto> Handle(GetMyReportByIdQuery request, CancellationToken cancellationToken)
    {
        // Loaded before the check, not after: whether the caller may run it depends on the
        // query group it is filed in, which is part of the entity.
        var report = await _unitOfWork.Reports.GetByIdAsync(
            request.Id, cancellationToken, ReportMapper.FullIncludes)
            ?? throw new NotFoundException(nameof(Report), request.Id);

        await ReportAccess.EnsureCanRunAsync(report, _unitOfWork, _currentUser, cancellationToken);

        if (!report.IsEnabled)
            throw new DomainException("This report is currently disabled.");

        var held = await _permissions.GetForRolesAsync(_currentUser.Roles, cancellationToken);
        return ReportMapper.ToDto(report, held);
    }
}

// ---------------------------------------------------------------- access

/// <summary>Who a report is granted to.</summary>
public class GetReportAccessQuery : IRequest<ReportAccessDto>
{
    public Guid Id { get; set; }
}

public class GetReportAccessQueryHandler : IRequestHandler<GetReportAccessQuery, ReportAccessDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetReportAccessQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<ReportAccessDto> Handle(GetReportAccessQuery request, CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.Reports.ExistsAsync(r => r.Id == request.Id, cancellationToken);
        if (!exists)
            throw new NotFoundException(nameof(Report), request.Id);

        var roles = await _unitOfWork.ReportRoles.FindAsync(r => r.ReportId == request.Id, cancellationToken);
        var groups = await _unitOfWork.ReportUserGroups.FindAsync(g => g.ReportId == request.Id, cancellationToken);
        var users = await _unitOfWork.ReportUsers.FindAsync(u => u.ReportId == request.Id, cancellationToken);

        return new ReportAccessDto
        {
            RoleIds = roles.Select(r => r.RoleId).ToList(),
            UserGroupIds = groups.Select(g => g.UserGroupId).ToList(),
            UserIds = users.Select(u => u.UserId).ToList()
        };
    }
}
