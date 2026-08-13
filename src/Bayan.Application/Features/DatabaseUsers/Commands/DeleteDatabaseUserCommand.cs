using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DatabaseUsers.Commands;

public class DeleteDatabaseUserCommand : IRequest
{
    public Guid Id { get; set; }
    public DeleteDatabaseUserCommand(Guid id) => Id = id;
}

public class DeleteDatabaseUserCommandHandler : IRequestHandler<DeleteDatabaseUserCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDatabaseUserCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(DeleteDatabaseUserCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.DatabaseUsers.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
            throw new NotFoundException(nameof(DatabaseUser), request.Id);

        _unitOfWork.DatabaseUsers.Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
