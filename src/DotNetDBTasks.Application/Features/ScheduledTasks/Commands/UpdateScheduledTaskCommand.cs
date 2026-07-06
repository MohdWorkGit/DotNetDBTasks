using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using DotNetDBTasks.Domain.Services;
using MediatR;

namespace DotNetDBTasks.Application.Features.ScheduledTasks.Commands;

/// <summary>
/// Updates a scheduled export task, replacing its items and viewer permissions
/// wholesale and recomputing the next trigger time (Admin only — enforced at the
/// controller).
/// </summary>
public class UpdateScheduledTaskCommand : IRequest<ScheduledTaskDto>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string OutputFolder { get; set; } = string.Empty;
    public ScheduleFrequency Frequency { get; set; }
    public int? IntervalMinutes { get; set; }
    public string? TimeOfDay { get; set; }
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public List<ScheduledTaskItemInput> Items { get; set; } = new();
    public List<Guid> ViewerUserIds { get; set; } = new();
}

public class UpdateScheduledTaskCommandHandler : IRequestHandler<UpdateScheduledTaskCommand, ScheduledTaskDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdateScheduledTaskCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ScheduledTaskDto> Handle(UpdateScheduledTaskCommand request, CancellationToken cancellationToken)
    {
        var task = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == request.Id, cancellationToken, "Items", "Viewers")).FirstOrDefault()
            ?? throw new NotFoundException(nameof(ScheduledTask), request.Id);

        await ScheduledTaskInputValidator.ValidateAsync(
            _unitOfWork,
            request.Name,
            request.OutputFolder,
            request.Frequency,
            request.IntervalMinutes,
            request.TimeOfDay,
            request.DayOfWeek,
            request.DayOfMonth,
            request.Items,
            request.ViewerUserIds,
            excludeTaskId: task.Id,
            cancellationToken);

        task.Name = request.Name.Trim();
        // Oracle stores '' as NULL, so blank descriptions are persisted as null.
        task.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description;
        task.IsEnabled = request.IsEnabled;
        task.OutputFolder = request.OutputFolder.Trim();
        task.Frequency = request.Frequency;
        task.IntervalMinutes = request.IntervalMinutes;
        task.TimeOfDay = request.TimeOfDay;
        task.DayOfWeek = request.DayOfWeek;
        task.DayOfMonth = request.DayOfMonth;
        task.UpdatedAt = DateTime.UtcNow;
        task.NextRunAt = ScheduleCalculator.ComputeNextRunUtc(task, DateTime.Now);

        // Items are replaced wholesale; carry saved checkpoints over to the new items
        // (matched by query + key config) so an edit doesn't restart incremental runs.
        var previousKeys = task.Items
            .Where(i => i.LastKeyValue is not null)
            .GroupBy(i => ScheduledTaskInputValidator.CheckpointIdentity(i.DynamicQueryId, i.KeyColumn, i.KeyParameter))
            .ToDictionary(g => g.Key, g => g.First().LastKeyValue!);

        foreach (var item in task.Items.ToList())
            _unitOfWork.ScheduledTaskItems.Delete(item);
        foreach (var item in ScheduledTaskInputValidator.BuildItems(task.Id, request.Items, previousKeys))
            await _unitOfWork.ScheduledTaskItems.AddAsync(item, cancellationToken);

        foreach (var viewer in task.Viewers.ToList())
            _unitOfWork.ScheduledTaskViewers.Delete(viewer);
        foreach (var userId in request.ViewerUserIds.Distinct())
        {
            await _unitOfWork.ScheduledTaskViewers.AddAsync(
                new ScheduledTaskViewer { ScheduledTaskId = task.Id, UserId = userId }, cancellationToken);
        }

        _unitOfWork.ScheduledTasks.Update(task);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == task.Id, cancellationToken,
            "Items", "Items.DynamicQuery", "Viewers", "Viewers.User")).First();
        return ScheduledTaskMapper.ToDto(updated);
    }
}
