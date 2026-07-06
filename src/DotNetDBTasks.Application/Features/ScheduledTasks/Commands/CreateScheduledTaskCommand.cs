using System.Text.Json;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using DotNetDBTasks.Domain.Services;
using MediatR;

namespace DotNetDBTasks.Application.Features.ScheduledTasks.Commands;

/// <summary>
/// One query inside a scheduled task, as sent by the admin form.
/// </summary>
public class ScheduledTaskItemInput
{
    public Guid DynamicQueryId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public ExportFileFormat ExportFormat { get; set; }
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
/// Creates a scheduled export task (Admin only — enforced at the controller).
/// </summary>
public class CreateScheduledTaskCommand : IRequest<ScheduledTaskDto>
{
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
            request.Frequency,
            request.IntervalMinutes,
            request.TimeOfDay,
            request.DayOfWeek,
            request.DayOfMonth,
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
            Frequency = request.Frequency,
            IntervalMinutes = request.IntervalMinutes,
            TimeOfDay = request.TimeOfDay,
            DayOfWeek = request.DayOfWeek,
            DayOfMonth = request.DayOfMonth,
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };
        task.NextRunAt = ScheduleCalculator.ComputeNextRunUtc(task, DateTime.Now);

        foreach (var item in ScheduledTaskInputValidator.BuildItems(task.Id, request.Items))
            task.Items.Add(item);

        foreach (var userId in request.ViewerUserIds.Distinct())
            task.Viewers.Add(new ScheduledTaskViewer { ScheduledTaskId = task.Id, UserId = userId });

        await _unitOfWork.ScheduledTasks.AddAsync(task, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var created = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == task.Id, cancellationToken,
            "Items", "Items.DynamicQuery", "Viewers", "Viewers.User")).First();
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
        ScheduleFrequency frequency,
        int? intervalMinutes,
        string? timeOfDay,
        int? dayOfWeek,
        int? dayOfMonth,
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

        switch (frequency)
        {
            case ScheduleFrequency.EveryNMinutes when intervalMinutes is null or < 1:
                throw new DomainException("Interval (minutes) must be at least 1.");
            case ScheduleFrequency.Daily or ScheduleFrequency.Weekly or ScheduleFrequency.Monthly
                when !TimeSpan.TryParse(timeOfDay, out _):
                throw new DomainException("Time of day must be in HH:mm format.");
            case ScheduleFrequency.Weekly when dayOfWeek is null or < 0 or > 6:
                throw new DomainException("Day of week must be between 0 (Sunday) and 6 (Saturday).");
            case ScheduleFrequency.Monthly when dayOfMonth is null or < 1 or > 31:
                throw new DomainException("Day of month must be between 1 and 31.");
        }

        if (items.Count == 0)
            throw new DomainException("A scheduled task must contain at least one query.");

        foreach (var item in items)
        {
            var query = await unitOfWork.DynamicQueries.GetByIdAsync(item.DynamicQueryId, cancellationToken)
                ?? throw new DomainException("One of the selected queries no longer exists.");

            // Write queries are allowed (they commit and record affected rows, no file),
            // but an incremental checkpoint only makes sense for a query that returns rows.
            if (string.IsNullOrWhiteSpace(item.KeyColumn))
                continue;

            if (IsWriteQuery(query.SqlQuery))
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

    private static bool IsWriteQuery(string sql)
    {
        var trimmed = sql.TrimStart();
        return trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
    }
}
