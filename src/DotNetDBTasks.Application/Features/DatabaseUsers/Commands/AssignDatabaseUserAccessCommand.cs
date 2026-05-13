using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DatabaseUsers.Commands;

/// <summary>
/// Replaces the set of roles that are allowed to use a given database user.
/// </summary>
public class AssignDatabaseUserAccessCommand : IRequest
{
    public Guid DatabaseUserId { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
}

public class AssignDatabaseUserAccessCommandHandler : IRequestHandler<AssignDatabaseUserAccessCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignDatabaseUserAccessCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(AssignDatabaseUserAccessCommand request, CancellationToken cancellationToken)
    {
        var dbUser = await _unitOfWork.DatabaseUsers.GetByIdAsync(request.DatabaseUserId, cancellationToken);
        if (dbUser is null)
            throw new NotFoundException(nameof(DatabaseUser), request.DatabaseUserId);

        var existing = await _unitOfWork.DatabaseUserRoleAccess.FindAsync(
            a => a.DatabaseUserId == request.DatabaseUserId, cancellationToken);
        foreach (var entry in existing)
            _unitOfWork.DatabaseUserRoleAccess.Delete(entry);

        foreach (var roleId in request.RoleIds.Distinct())
        {
            await _unitOfWork.DatabaseUserRoleAccess.AddAsync(new DatabaseUserRoleAccess
            {
                RoleId = roleId,
                DatabaseUserId = request.DatabaseUserId
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
