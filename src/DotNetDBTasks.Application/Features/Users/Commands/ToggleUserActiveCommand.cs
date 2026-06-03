using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.Users.Commands;

public class ToggleUserActiveCommand : IRequest
{
    public Guid UserId { get; set; }
    public bool IsActive { get; set; }
}

public class ToggleUserActiveCommandHandler : IRequestHandler<ToggleUserActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public ToggleUserActiveCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ToggleUserActiveCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw new NotFoundException("User", request.UserId);

        // Guard: never allow the last active administrator to be deactivated,
        // otherwise the system could be left with no one able to manage it.
        if (!request.IsActive && user.IsActive)
            await EnsureNotLastActiveAdminAsync(user, cancellationToken);

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Throws if <paramref name="user"/> is an administrator and no other active
    /// administrator would remain after deactivation.
    /// </summary>
    private async Task EnsureNotLastActiveAdminAsync(User user, CancellationToken cancellationToken)
    {
        var adminRole = (await _unitOfWork.Roles.FindAsync(
            r => r.Name == "Admin", cancellationToken)).FirstOrDefault();
        if (adminRole is null)
            return;

        var targetIsAdmin = await _unitOfWork.UserRoles.ExistsAsync(
            ur => ur.UserId == user.Id && ur.RoleId == adminRole.Id, cancellationToken);
        if (!targetIsAdmin)
            return;

        var adminUserIds = (await _unitOfWork.UserRoles.FindAsync(
                ur => ur.RoleId == adminRole.Id, cancellationToken))
            .Select(ur => ur.UserId)
            .Where(id => id != user.Id)
            .ToList();

        var hasOtherActiveAdmin = adminUserIds.Count > 0 && await _unitOfWork.Users.ExistsAsync(
            u => adminUserIds.Contains(u.Id) && u.IsActive, cancellationToken);

        if (!hasOtherActiveAdmin)
            throw new DomainException("Cannot deactivate the last active administrator.");
    }
}
