using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DatabaseUsers.Commands;

/// <summary>
/// Replaces the set of application users that are allowed to use a given database user.
/// </summary>
public class AssignDatabaseUserAccessCommand : IRequest
{
    public Guid DatabaseUserId { get; set; }
    public List<Guid> UserIds { get; set; } = new();
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

        // Remove existing access entries
        var existing = await _unitOfWork.UserDatabaseUserAccess.FindAsync(
            a => a.DatabaseUserId == request.DatabaseUserId, cancellationToken);
        foreach (var entry in existing)
            _unitOfWork.UserDatabaseUserAccess.Delete(entry);

        // Add new access entries
        foreach (var userId in request.UserIds)
        {
            await _unitOfWork.UserDatabaseUserAccess.AddAsync(new UserDatabaseUserAccess
            {
                UserId = userId,
                DatabaseUserId = request.DatabaseUserId
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
