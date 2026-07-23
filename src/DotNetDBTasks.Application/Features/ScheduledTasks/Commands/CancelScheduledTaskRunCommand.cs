using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.ScheduledTasks.Commands;

/// <summary>
/// Requests cancellation of an in-progress scheduled-task run. Returns true when a matching run was
/// actively executing on this instance and was signaled to stop (which aborts its running query);
/// false when no such run is currently in flight (already finished, or not running here).
/// </summary>
public record CancelScheduledTaskRunCommand(Guid TaskId, Guid RunId) : IRequest<bool>;

public class CancelScheduledTaskRunCommandHandler : IRequestHandler<CancelScheduledTaskRunCommand, bool>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IScheduledTaskRunRegistry _runRegistry;

    public CancelScheduledTaskRunCommandHandler(
        IUnitOfWork unitOfWork,
        IScheduledTaskRunRegistry runRegistry)
    {
        _unitOfWork = unitOfWork;
        _runRegistry = runRegistry;
    }

    public async Task<bool> Handle(CancelScheduledTaskRunCommand request, CancellationToken cancellationToken)
    {
        if (!await _unitOfWork.ScheduledTasks.ExistsAsync(t => t.Id == request.TaskId, cancellationToken))
            throw new NotFoundException(nameof(ScheduledTask), request.TaskId);

        var run = (await _unitOfWork.ScheduledTaskRuns.FindAsync(
                r => r.Id == request.RunId && r.ScheduledTaskId == request.TaskId, cancellationToken))
            .FirstOrDefault()
            ?? throw new NotFoundException(nameof(ScheduledTaskRun), request.RunId);

        // Registry is authoritative for what is actually running; the DB row may already be terminal.
        return _runRegistry.Cancel(run.Id);
    }
}
