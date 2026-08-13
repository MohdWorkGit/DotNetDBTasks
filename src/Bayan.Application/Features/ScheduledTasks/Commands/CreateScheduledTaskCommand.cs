using System.Text.Json;
using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using Bayan.Domain.Services;
using MediatR;

namespace Bayan.Application.Features.ScheduledTasks.Commands;

/// <summary>
/// One query inside a scheduled task, as sent by the admin form.
/// </summary>
public class ScheduledTaskItemInput
{
    public Guid DynamicQueryId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public ExportFileFormat ExportFormat { get; set; }

    /// <summary>CSV only: field separator text (null/empty = comma), e.g. ";" or ";;". Also accepts "tab", "semicolon", "pipe".</summary>
    public string? CsvSeparator { get; set; }

    public string? FileNamePrefix { get; set; }
    public bool AppendTimestamp { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Optional incremental checkpoint (read queries only): result column to track.</summary>
    public string? KeyColumn { get; set; }

    /// <summary>The query parameter that receives the saved key.</summary>
    public string? KeyParameter { get; set; }

    /// <summary>Key value used before any checkpoint exists.</summary>
    public string? InitialKey { get; set; }

    /// <summary>When true (edit only), the saved checkpoint is cleared so the next run starts from InitialKey.</summary>
    public bool ResetKey { get; set; }
}

/// <summary>
/// One recurrence rule of a scheduled task, as sent by the admin form. A task can
/// have several (e.g. daily at 03:00 + monthly on day 14 + daily at 14:00).
/// </summary>
public class ScheduledTaskTriggerInput
{
    public ScheduleFrequency Frequency { get; set; }
    public int? IntervalMinutes { get; set; }
    public string? TimeOfDay { get; set; }
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Creates a scheduled export task (Admin only — enforced at the controller).
/// </summary>
public class CreateScheduledTaskCommand : IRequest<ScheduledTaskDto>
{
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

public class CreateScheduledTaskCommandHandler : IRequestHandler<CreateScheduledTaskCommand, ScheduledTaskDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public CreateScheduledTaskCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<ScheduledTaskDto> Handle(CreateScheduledTaskCommand request, CancellationToken cancellationToken)
    {
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
            excludeTaskId: null,
            cancellationToken);

        var task = new ScheduledTask
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            // Oracle stores '' as NULL, so blank descriptions are persisted as null.
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
            IsEnabled = request.IsEnabled,
            OutputFolder = request.OutputFolder.Trim(),
            ArchiveFolder = string.IsNullOrWhiteSpace(request.ArchiveFolder) ? null : request.ArchiveFolder.Trim(),
            CombineOutput = request.CombineOutput,
            IncludeHeaders = request.IncludeHeaders,
            CombinedFileName = string.IsNullOrWhiteSpace(request.CombinedFileName) ? null : request.CombinedFileName.Trim(),
            CombinedFormat = request.CombinedFormat,
            CombinedCsvSeparator = ScheduledTaskInputValidator.NormalizeSeparator(request.CombinedCsvSeparator, request.CombinedFormat),
            CombinedAppendTimestamp = request.CombinedAppendTimestamp,
            TimestampFormat = string.IsNullOrWhiteSpace(request.TimestampFormat) ? null : request.TimestampFormat.Trim(),
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var trigger in ScheduledTaskInputValidator.BuildTriggers(task.Id, request.Triggers))
            task.Triggers.Add(trigger);
        task.NextRunAt = ScheduleCalculator.ComputeNextRunUtc(task.IsEnabled, task.Triggers, DateTime.Now);

        foreach (var item in ScheduledTaskInputValidator.BuildItems(task.Id, request.Items))
            task.Items.Add(item);

        var downloadUserIds = request.DownloadUserIds.ToHashSet();
        foreach (var userId in request.ViewerUserIds.Distinct())
        {
            task.Viewers.Add(new ScheduledTaskViewer
            {
                ScheduledTaskId = task.Id,
                UserId = userId,
                CanDownloadFiles = downloadUserIds.Contains(userId)
            });
        }

        await _unitOfWork.ScheduledTasks.AddAsync(task, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var created = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == task.Id, cancellationToken,
            "Triggers", "Items", "Items.DynamicQuery", "Viewers", "Viewers.User")).First();
        return ScheduledTaskMapper.ToDto(created);
    }
}

/// <summary>
/// Shared validation and item construction for create/update of scheduled tasks.
/// Throws <see cref="DomainException"/> with a user-facing message on any violation.
/// </summary>
public static class ScheduledTaskInputValidator
{
    public static async Task ValidateAsync(
        IUnitOfWork unitOfWork,
        string name,
        string outputFolder,
        string? archiveFolder,
        bool combineOutput,
        string? combinedCsvSeparator,
        string? timestampFormat,
        List<ScheduledTaskTriggerInput> triggers,
        List<ScheduledTaskItemInput> items,
        List<Guid> viewerUserIds,
        Guid? excludeTaskId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Task name is required.");

        var trimmedName = name.Trim();
        var nameTaken = await unitOfWork.ScheduledTasks.ExistsAsync(
            t => t.Name == trimmedName && (excludeTaskId == null || t.Id != excludeTaskId),
            cancellationToken);
        if (nameTaken)
            throw new DomainException($"A scheduled task named '{trimmedName}' already exists.");

        if (string.IsNullOrWhiteSpace(outputFolder) || !Path.IsPathRooted(outputFolder.Trim()))
            throw new DomainException("Output folder must be an absolute path on the server (e.g. D:\\Exports\\Sales).");

        if (!string.IsNullOrWhiteSpace(archiveFolder) && !Path.IsPathRooted(archiveFolder.Trim()))
            throw new DomainException("Archive folder must be an absolute path on the server (e.g. D:\\Exports\\Archive).");

        if (combineOutput && !Common.Models.CsvSeparator.TryParse(combinedCsvSeparator, out _))
            throw new DomainException(
                $"The combined file's CSV separator must be 1–{Common.Models.CsvSeparator.MaxLength} characters and cannot contain quotes or line breaks.");

        if (!string.IsNullOrWhiteSpace(timestampFormat))
        {
            var format = timestampFormat.Trim();
            if (format.Length > 50)
                throw new DomainException("The timestamp format cannot exceed 50 characters.");
            string sample;
            try
            {
                sample = DateTime.Now.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                throw new DomainException($"'{format}' is not a valid .NET date format for the file-name timestamp.");
            }
            if (sample.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new DomainException(
                    $"The timestamp format produces '{sample}', which contains characters not allowed in file names.");
        }

        if (triggers.Count == 0)
            throw new DomainException("A scheduled task must have at least one trigger.");

        foreach (var trigger in triggers)
        {
            switch (trigger.Frequency)
            {
                case ScheduleFrequency.EveryNMinutes when trigger.IntervalMinutes is null or < 1:
                    throw new DomainException("Interval (minutes) must be at least 1.");
                case ScheduleFrequency.Daily or ScheduleFrequency.Weekly or ScheduleFrequency.Monthly
                    when !TimeSpan.TryParse(trigger.TimeOfDay, out _):
                    throw new DomainException("Time of day must be in HH:mm format.");
                case ScheduleFrequency.Weekly when trigger.DayOfWeek is null or < 0 or > 6:
                    throw new DomainException("Day of week must be between 0 (Sunday) and 6 (Saturday).");
                case ScheduleFrequency.Monthly when trigger.DayOfMonth is null or < 1 or > 31:
                    throw new DomainException("Day of month must be between 1 and 31.");
            }
        }

        if (items.Count == 0)
            throw new DomainException("A scheduled task must contain at least one query.");

        foreach (var item in items)
        {
            var query = await unitOfWork.DynamicQueries.GetByIdAsync(item.DynamicQueryId, cancellationToken)
                ?? throw new DomainException("One of the selected queries no longer exists.");

            if (!Common.Models.CsvSeparator.TryParse(item.CsvSeparator, out _))
                throw new DomainException(
                    $"Query '{query.Name}': the CSV separator must be 1–{Common.Models.CsvSeparator.MaxLength} characters and cannot contain quotes or line breaks.");

            // Write queries are allowed (they commit and record affected rows, no file),
            // but an incremental checkpoint only makes sense for a query that returns rows.
            if (string.IsNullOrWhiteSpace(item.KeyColumn))
                continue;

            if (query.QueryType.IsWrite())
                throw new DomainException(
                    $"Query '{query.Name}' modifies data; a key column checkpoint is only supported for read queries.");

            if (string.IsNullOrWhiteSpace(item.KeyParameter))
                throw new DomainException(
                    $"Query '{query.Name}': choose which query parameter receives the saved key value.");

            var parameterExists = await unitOfWork.QueryParameters.ExistsAsync(
                p => p.DynamicQueryId == item.DynamicQueryId && p.Name == item.KeyParameter,
                cancellationToken);
            if (!parameterExists)
                throw new DomainException(
                    $"Query '{query.Name}' has no parameter named '{item.KeyParameter}' to receive the saved key.");

            if (string.IsNullOrWhiteSpace(item.InitialKey))
                throw new DomainException(
                    $"Query '{query.Name}': an initial key value is required for the first incremental run.");
        }

        foreach (var userId in viewerUserIds.Distinct())
        {
            if (!await unitOfWork.Users.ExistsAsync(u => u.Id == userId, cancellationToken))
                throw new DomainException("One of the selected viewer users no longer exists.");
        }
    }

    /// <summary>
    /// Builds trigger entities from the form input. Fields that don't apply to a
    /// trigger's frequency are stored as null regardless of what the form sent.
    /// </summary>
    public static IEnumerable<ScheduledTaskTrigger> BuildTriggers(
        Guid taskId, List<ScheduledTaskTriggerInput> triggers) =>
        triggers.Select(input => new ScheduledTaskTrigger
        {
            Id = Guid.NewGuid(),
            ScheduledTaskId = taskId,
            Frequency = input.Frequency,
            IntervalMinutes = input.Frequency == ScheduleFrequency.EveryNMinutes ? input.IntervalMinutes : null,
            TimeOfDay = input.Frequency == ScheduleFrequency.EveryNMinutes ? null : input.TimeOfDay,
            DayOfWeek = input.Frequency == ScheduleFrequency.Weekly ? input.DayOfWeek : null,
            DayOfMonth = input.Frequency == ScheduleFrequency.Monthly ? input.DayOfMonth : null,
            SortOrder = input.SortOrder,
            CreatedAt = DateTime.UtcNow
        });

    /// <summary>
    /// Builds item entities from the form input. <paramref name="previousKeys"/> carries the
    /// saved checkpoints of the task's existing items (update replaces items wholesale, so
    /// without this every edit would silently restart incremental queries from InitialKey).
    /// </summary>
    public static IEnumerable<ScheduledTaskItem> BuildItems(
        Guid taskId,
        List<ScheduledTaskItemInput> items,
        IReadOnlyDictionary<string, string>? previousKeys = null) =>
        items.Select(input => new ScheduledTaskItem
        {
            Id = Guid.NewGuid(),
            ScheduledTaskId = taskId,
            DynamicQueryId = input.DynamicQueryId,
            ParametersJson = JsonSerializer.Serialize(input.Parameters ?? new Dictionary<string, string>()),
            ExportFormat = input.ExportFormat,
            CsvSeparator = NormalizeSeparator(input.CsvSeparator, input.ExportFormat),
            FileNamePrefix = string.IsNullOrWhiteSpace(input.FileNamePrefix) ? null : input.FileNamePrefix.Trim(),
            AppendTimestamp = input.AppendTimestamp,
            SortOrder = input.SortOrder,
            KeyColumn = Clean(input.KeyColumn),
            KeyParameter = string.IsNullOrWhiteSpace(input.KeyColumn) ? null : Clean(input.KeyParameter),
            InitialKey = string.IsNullOrWhiteSpace(input.KeyColumn) ? null : Clean(input.InitialKey),
            LastKeyValue = !input.ResetKey
                && previousKeys is not null
                && previousKeys.TryGetValue(CheckpointIdentity(input.DynamicQueryId, input.KeyColumn, input.KeyParameter), out var saved)
                    ? saved
                    : null,
            CreatedAt = DateTime.UtcNow
        });

    /// <summary>
    /// A checkpoint survives an edit only while it still means the same thing: same
    /// query, same key column, same receiving parameter.
    /// </summary>
    public static string CheckpointIdentity(Guid dynamicQueryId, string? keyColumn, string? keyParameter) =>
        $"{dynamicQueryId:N}|{keyColumn?.Trim().ToLowerInvariant()}|{keyParameter?.Trim().ToLowerInvariant()}";

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A separator is stored only for CSV output and only when it differs from the
    /// default comma. Compared without trimming — a tab separator is itself whitespace.
    /// </summary>
    public static string? NormalizeSeparator(string? separator, ExportFileFormat format) =>
        format == ExportFileFormat.Csv
            && !string.IsNullOrEmpty(separator)
            && Common.Models.CsvSeparator.Parse(separator) != Common.Models.CsvSeparator.Default
                ? separator
                : null;

}
