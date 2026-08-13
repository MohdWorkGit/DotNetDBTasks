using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.ScheduledTasks.Commands;

/// <summary>
/// Deletes a scheduled task along with its items, run history and viewer permissions
/// (cascade). Admin only — enforced at the controller.
/// </summary>
public record DeleteScheduledTaskCommand(Guid Id) : IRequest<Unit>;

public class DeleteScheduledTaskCommandHandler : IRequestHandler<DeleteScheduledTaskCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteScheduledTaskCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(DeleteScheduledTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _unitOfWork.ScheduledTasks.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(ScheduledTask), request.Id);

        _unitOfWork.ScheduledTasks.Delete(task);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
