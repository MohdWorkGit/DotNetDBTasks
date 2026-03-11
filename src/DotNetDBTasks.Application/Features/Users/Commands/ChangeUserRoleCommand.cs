using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using FluentValidation;
using MediatR;

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

    public ChangeUserRoleCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ChangeUserRoleCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw new NotFoundException("User", request.UserId);

        // Validate all role IDs exist
        var allRoles = await _unitOfWork.Roles.GetAllAsync(cancellationToken);
        var validRoleIds = allRoles.Select(r => r.Id).ToHashSet();
        foreach (var roleId in request.RoleIds)
        {
            if (!validRoleIds.Contains(roleId))
                throw new InvalidOperationException($"Role with ID '{roleId}' does not exist.");
        }

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
}
