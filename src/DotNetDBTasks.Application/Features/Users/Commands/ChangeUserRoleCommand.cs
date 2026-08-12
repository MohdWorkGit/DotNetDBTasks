using DotNetDBTasks.Application.Common.Security;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using FluentValidation;
using MediatR;
using DotNetDBTasks.Application.Common.Interfaces;

namespace DotNetDBTasks.Application.Features.Users.Commands;

public class ChangeUserRoleCommand : IRequest
{
    public Guid UserId { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
}

public class ChangeUserRoleValidator : AbstractValidator<ChangeUserRoleCommand>
{
    public ChangeUserRoleValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleIds).NotEmpty().WithMessage("At least one role is required.");
    }
}

public class ChangeUserRoleCommandHandler : IRequestHandler<ChangeUserRoleCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AdminAccountGuard _adminGuard;
    private readonly IAppLocalizer _messages;

    public ChangeUserRoleCommandHandler(IUnitOfWork unitOfWork, AdminAccountGuard adminGuard, IAppLocalizer messages)
    {
        _unitOfWork = unitOfWork;
        _adminGuard = adminGuard;
        _messages = messages;
    }

    public async Task Handle(ChangeUserRoleCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw new NotFoundException("User", request.UserId);

        // Three separate escalation paths to close: demoting/altering an existing administrator,
        // promoting any account to Admin, and a non-Admin rewriting their own role set — which
        // would let an Access Manager grant themselves query access they are defined not to have.
        await _adminGuard.EnsureCanModifyUserAsync(request.UserId, cancellationToken);
        await _adminGuard.EnsureCanAssignRolesAsync(request.RoleIds, cancellationToken);
        _adminGuard.EnsureNotSelfRoleChange(request.UserId);

        // Validate all role IDs exist
        var allRoles = await _unitOfWork.Roles.GetAllAsync(cancellationToken);
        var validRoleIds = allRoles.Select(r => r.Id).ToHashSet();
        foreach (var roleId in request.RoleIds)
        {
            if (!validRoleIds.Contains(roleId))
                throw new DomainException($"Role with ID '{roleId}' does not exist.");
        }

        // Guard: never allow the last active administrator to lose the Admin role,
        // otherwise the system could be left with no one able to manage it.
        var adminRole = allRoles.FirstOrDefault(r => r.Name == "Admin");
        if (adminRole is not null && !request.RoleIds.Contains(adminRole.Id))
            await EnsureNotLastActiveAdminAsync(user, adminRole.Id, cancellationToken);

        // Remove existing roles
        var existingUserRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == request.UserId, cancellationToken);
        foreach (var ur in existingUserRoles)
            _unitOfWork.UserRoles.Delete(ur);

        // Add new roles
        foreach (var roleId in request.RoleIds)
        {
            await _unitOfWork.UserRoles.AddAsync(new UserRole
            {
                UserId = request.UserId,
                RoleId = roleId
            }, cancellationToken);
        }

        user.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Throws if <paramref name="user"/> is currently the only active administrator,
    /// preventing a role change that would strip the system of its last admin.
    /// </summary>
    private async Task EnsureNotLastActiveAdminAsync(User user, Guid adminRoleId, CancellationToken cancellationToken)
    {
        var targetIsAdmin = await _unitOfWork.UserRoles.ExistsAsync(
            ur => ur.UserId == user.Id && ur.RoleId == adminRoleId, cancellationToken);
        if (!targetIsAdmin || !user.IsActive)
            return;

        var adminUserIds = (await _unitOfWork.UserRoles.FindAsync(
                ur => ur.RoleId == adminRoleId, cancellationToken))
            .Select(ur => ur.UserId)
            .Where(id => id != user.Id)
            .ToList();

        var hasOtherActiveAdmin = adminUserIds.Count > 0 && await _unitOfWork.Users.ExistsAsync(
            u => adminUserIds.Contains(u.Id) && u.IsActive, cancellationToken);

        if (!hasOtherActiveAdmin)
            throw new DomainException(_messages[MessageKeys.CannotRemoveLastAdmin]);
    }
}
