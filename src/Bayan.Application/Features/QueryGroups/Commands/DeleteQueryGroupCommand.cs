using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.QueryGroups.Commands;

/// <summary>
/// Deletes a query group. Queries inside the group are NOT deleted — their QueryGroupId
/// is cleared by the SetNull cascade so they become "ungrouped".
/// </summary>
public record DeleteQueryGroupCommand(Guid Id) : IRequest<Unit>;

public class DeleteQueryGroupCommandHandler : IRequestHandler<DeleteQueryGroupCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteQueryGroupCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(DeleteQueryGroupCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.QueryGroups.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
            throw new NotFoundException(nameof(Domain.Entities.QueryGroup), request.Id);

        _unitOfWork.QueryGroups.Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
