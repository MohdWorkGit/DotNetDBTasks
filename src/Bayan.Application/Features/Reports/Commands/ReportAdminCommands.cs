using Bayan.Application.Features.Reports.Dtos;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Reports.Commands;

/// <summary>
/// Deletes a report. Its datasets, parameters, maps, grants and run log go with it by cascade;
/// the queries it referenced are untouched.
/// </summary>
public class DeleteReportCommand : IRequest<Unit>
{
    public Guid Id { get; set; }
}

public class DeleteReportCommandHandler : IRequestHandler<DeleteReportCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteReportCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Unit> Handle(DeleteReportCommand request, CancellationToken cancellationToken)
    {
        var report = await _unitOfWork.Reports.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Report), request.Id);

        _unitOfWork.Reports.Delete(report);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

// ----------------------------------------------------------------------------

/// <summary>
/// Replaces who a report is granted to. All three lists are sent together and applied as one
/// set, so the page always describes the whole grant rather than a delta that can be applied
/// out of order.
/// </summary>
public class SetReportAccessCommand : IRequest<ReportAccessDto>
{
    public Guid ReportId { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
    public List<Guid> UserGroupIds { get; set; } = new();
    public List<Guid> UserIds { get; set; } = new();
}

public class SetReportAccessCommandHandler : IRequestHandler<SetReportAccessCommand, ReportAccessDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public SetReportAccessCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<ReportAccessDto> Handle(
        SetReportAccessCommand request,
        CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.Reports.ExistsAsync(r => r.Id == request.ReportId, cancellationToken);
        if (!exists)
            throw new NotFoundException(nameof(Report), request.ReportId);

        var existingRoles = await _unitOfWork.ReportRoles.FindAsync(
            r => r.ReportId == request.ReportId, cancellationToken);
        foreach (var grant in existingRoles)
            _unitOfWork.ReportRoles.Delete(grant);

        var existingGroups = await _unitOfWork.ReportUserGroups.FindAsync(
            g => g.ReportId == request.ReportId, cancellationToken);
        foreach (var grant in existingGroups)
            _unitOfWork.ReportUserGroups.Delete(grant);

        var existingUsers = await _unitOfWork.ReportUsers.FindAsync(
            u => u.ReportId == request.ReportId, cancellationToken);
        foreach (var grant in existingUsers)
            _unitOfWork.ReportUsers.Delete(grant);

        foreach (var roleId in request.RoleIds.Distinct())
        {
            await _unitOfWork.ReportRoles.AddAsync(
                new ReportRole { ReportId = request.ReportId, RoleId = roleId }, cancellationToken);
        }

        foreach (var groupId in request.UserGroupIds.Distinct())
        {
            await _unitOfWork.ReportUserGroups.AddAsync(
                new ReportUserGroup { ReportId = request.ReportId, UserGroupId = groupId }, cancellationToken);
        }

        foreach (var userId in request.UserIds.Distinct())
        {
            await _unitOfWork.ReportUsers.AddAsync(
                new ReportUser { ReportId = request.ReportId, UserId = userId }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ReportAccessDto
        {
            RoleIds = request.RoleIds.Distinct().ToList(),
            UserGroupIds = request.UserGroupIds.Distinct().ToList(),
            UserIds = request.UserIds.Distinct().ToList()
        };
    }
}
