using Bayan.Application.Common.Security;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.UserGroups.Commands;

/// <summary>
/// Replaces a group's membership with exactly the users given. Removing someone here takes
/// away every query and query group the group grants them, at once — which is the point of
/// having groups, and why this is its own audited action.
/// </summary>
public class SetUserGroupMembersCommand : IRequest<Unit>
{
    public Guid GroupId { get; set; }
    public List<Guid> UserIds { get; set; } = new();
}

public class SetUserGroupMembersCommandHandler : IRequestHandler<SetUserGroupMembersCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AdminAccountGuard _adminGuard;

    public SetUserGroupMembersCommandHandler(IUnitOfWork unitOfWork, AdminAccountGuard adminGuard)
    {
        _unitOfWork = unitOfWork;
        _adminGuard = adminGuard;
    }

    public async Task<Unit> Handle(SetUserGroupMembersCommand request, CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.UserGroups.GetByIdAsync(request.GroupId, cancellationToken);
        if (group is null)
            throw new NotFoundException(nameof(UserGroup), request.GroupId);

        var existing = await _unitOfWork.UserGroupMembers.FindAsync(
            m => m.UserGroupId == request.GroupId, cancellationToken);

        _adminGuard.EnsureNotJoiningGroup(
            request.UserIds, existing.Select(m => m.UserId).ToHashSet());

        foreach (var member in existing)
            _unitOfWork.UserGroupMembers.Delete(member);

        foreach (var userId in request.UserIds.Distinct())
        {
            await _unitOfWork.UserGroupMembers.AddAsync(
                new UserGroupMember { UserGroupId = request.GroupId, UserId = userId }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
