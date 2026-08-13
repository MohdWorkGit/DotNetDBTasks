using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.UserGroups.Commands;

/// <summary>
/// Deletes a user group. Membership rows and any query/query-group grants made to the group
/// go with it (cascade) — the users themselves are untouched.
/// </summary>
public record DeleteUserGroupCommand(Guid Id) : IRequest<Unit>;

public class DeleteUserGroupCommandHandler : IRequestHandler<DeleteUserGroupCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteUserGroupCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(DeleteUserGroupCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.UserGroups.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
            throw new NotFoundException(nameof(Domain.Entities.UserGroup), request.Id);

        _unitOfWork.UserGroups.Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
