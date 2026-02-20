using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Deletes a dynamic query by its identifier.
/// </summary>
public record DeleteDynamicQueryCommand(Guid Id) : IRequest<Unit>;

public class DeleteDynamicQueryCommandHandler : IRequestHandler<DeleteDynamicQueryCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDynamicQueryCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(DeleteDynamicQueryCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.DynamicQueries.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
            throw new NotFoundException(nameof(Domain.Entities.DynamicQuery), request.Id);

        _unitOfWork.DynamicQueries.Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
