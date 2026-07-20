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

    /// <summary>Optional second absolute folder that receives a copy of every export file.</summary>
    public string? ArchiveFolder { get; set; }

    /// <summary>When true, all read-query results are appended into one output file in item order.</summary>
    public bool CombineOutput { get; set; }

    /// <summary>When false, CSV/Excel exports contain data rows only (no header row).</summary>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>Combined mode: base file name without extension (null/empty = task name).</summary>
    public string? CombinedFileName { get; set; }

    /// <summary>Combined mode: format of the single output file.</summary>
    public ExportFileFormat CombinedFormat { get; set; } = ExportFileFormat.Csv;

    /// <summary>Combined mode, CSV only: field separator text (null/empty = comma).</summary>
    public string? CombinedCsvSeparator { get; set; }

    /// <summary>Combined mode: append a run timestamp to the file name (default true).</summary>
    public bool CombinedAppendTimestamp { get; set; } = true;

    /// <summary>.NET date format for the file-name timestamp suffix (null/empty = "_yyyyMMdd-HHmmss").</summary>
    public string? TimestampFormat { get; set; }

    public List<ScheduledTaskTriggerInput> Triggers { get; set; } = new();
    public List<ScheduledTaskItemInput> Items { get; set; } = new();
    public List<Guid> ViewerUserIds { get; set; } = new();

    /// <summary>Viewers who may also download the run's export files. Ids not present
    /// in <see cref="ViewerUserIds"/> are ignored (download implies view).</summary>
    public List<Guid> DownloadUserIds { get; set; } = new();
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
            t => t.Id == request.Id, cancellationToken, "Triggers", "Items", "Viewers")).FirstOrDefault()
            ?? throw new NotFoundException(nameof(ScheduledTask), request.Id);

        await ScheduledTaskInputValidator.ValidateAsync(
            _unitOfWork,
            request.Name,
            request.OutputFolder,
            request.ArchiveFolder,
            request.CombineOutput,
            request.CombinedCsvSeparator,
            request.TimestampFormat,
            request.Triggers,
            request.Items,
            request.ViewerUserIds,
            excludeTaskId: task.Id,
            cancellationToken);

        task.Name = request.Name.Trim();
        // Oracle stores '' as NULL, so blank descriptions are persisted as null.
        task.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description;
        task.IsEnabled = request.IsEnabled;
        task.OutputFolder = request.OutputFolder.Trim();
        task.ArchiveFolder = string.IsNullOrWhiteSpace(request.ArchiveFolder) ? null : request.ArchiveFolder.Trim();
        task.CombineOutput = request.CombineOutput;
        task.IncludeHeaders = request.IncludeHeaders;
        task.CombinedFileName = string.IsNullOrWhiteSpace(request.CombinedFileName) ? null : request.CombinedFileName.Trim();
        task.CombinedFormat = request.CombinedFormat;
        task.CombinedCsvSeparator = ScheduledTaskInputValidator.NormalizeSeparator(request.CombinedCsvSeparator, request.CombinedFormat);
        task.CombinedAppendTimestamp = request.CombinedAppendTimestamp;
        task.TimestampFormat = string.IsNullOrWhiteSpace(request.TimestampFormat) ? null : request.TimestampFormat.Trim();
        task.UpdatedAt = DateTime.UtcNow;

        // Triggers are replaced wholesale; the next run is the earliest occurrence
        // across the new set.
        var newTriggers = ScheduledTaskInputValidator.BuildTriggers(task.Id, request.Triggers).ToList();
        foreach (var trigger in task.Triggers.ToList())
            _unitOfWork.ScheduledTaskTriggers.Delete(trigger);
        foreach (var trigger in newTriggers)
            await _unitOfWork.ScheduledTaskTriggers.AddAsync(trigger, cancellationToken);
        task.NextRunAt = ScheduleCalculator.ComputeNextRunUtc(task.IsEnabled, newTriggers, DateTime.Now);

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
        var downloadUserIds = request.DownloadUserIds.ToHashSet();
        foreach (var userId in request.ViewerUserIds.Distinct())
        {
            await _unitOfWork.ScheduledTaskViewers.AddAsync(
                new ScheduledTaskViewer
                {
                    ScheduledTaskId = task.Id,
                    UserId = userId,
                    CanDownloadFiles = downloadUserIds.Contains(userId)
                }, cancellationToken);
        }

        _unitOfWork.ScheduledTasks.Update(task);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == task.Id, cancellationToken,
            "Triggers", "Items", "Items.DynamicQuery", "Viewers", "Viewers.User")).First();
        return ScheduledTaskMapper.ToDto(updated);
    }
}
