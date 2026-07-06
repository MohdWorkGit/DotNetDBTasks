using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.ScheduledTasks.Commands;

/// <summary>
/// Queues an immediate one-off run of a scheduled task, attributed to the requesting
/// admin. The run executes on the background worker; poll the task's runs for the
/// outcome. Works even while the task is disabled (useful for testing a setup).
/// </summary>
public record RunScheduledTaskNowCommand(Guid Id) : IRequest<Unit>;

public class RunScheduledTaskNowCommandHandler : IRequestHandler<RunScheduledTaskNowCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IScheduledTaskRunQueue _runQueue;
    private readonly ICurrentUserService _currentUser;

    public RunScheduledTaskNowCommandHandler(
        IUnitOfWork unitOfWork,
        IScheduledTaskRunQueue runQueue,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _runQueue = runQueue;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(RunScheduledTaskNowCommand request, CancellationToken cancellationToken)
    {
        if (!await _unitOfWork.ScheduledTasks.ExistsAsync(t => t.Id == request.Id, cancellationToken))
            throw new NotFoundException(nameof(ScheduledTask), request.Id);

        await _runQueue.EnqueueAsync(
            new ScheduledTaskRunRequest(request.Id, _currentUser.UserId, _currentUser.Username),
            cancellationToken);

        return Unit.Value;
    }
}
