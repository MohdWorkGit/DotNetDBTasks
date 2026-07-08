using System.Text.Json;
using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Application.Features.ScheduledTasks;

public class ScheduledTaskDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string OutputFolder { get; set; } = string.Empty;

    /// <summary>Optional second folder that receives a copy of every export file.</summary>
    public string? ArchiveFolder { get; set; }

    /// <summary>When true, all read-query results are appended into one output file in item order.</summary>
    public bool CombineOutput { get; set; }

    /// <summary>When false, CSV/Excel exports contain data rows only (no header row).</summary>
    public bool IncludeHeaders { get; set; } = true;

    public string? CombinedFileName { get; set; }
    public ExportFileFormat CombinedFormat { get; set; }
    public string? CombinedCsvSeparator { get; set; }
    public bool CombinedAppendTimestamp { get; set; }

    /// <summary>The task's recurrence rules; it fires on the earliest upcoming occurrence across all of them.</summary>
    public List<ScheduledTaskTriggerDto> Triggers { get; set; } = new();

    public DateTime? NextRunAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ScheduledTaskItemDto> Items { get; set; } = new();
    public List<ScheduledTaskViewerDto> Viewers { get; set; } = new();
    public ScheduledTaskRunDto? LastRun { get; set; }
}

public class ScheduledTaskTriggerDto
{
    public ScheduleFrequency Frequency { get; set; }
    public int? IntervalMinutes { get; set; }
    public string? TimeOfDay { get; set; }
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public int SortOrder { get; set; }
}

public class ScheduledTaskItemDto
{
    public Guid Id { get; set; }
    public Guid DynamicQueryId { get; set; }
    public string QueryName { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
    public ExportFileFormat ExportFormat { get; set; }

    /// <summary>CSV only: field separator character (null = comma).</summary>
    public string? CsvSeparator { get; set; }

    public string? FileNamePrefix { get; set; }
    public bool AppendTimestamp { get; set; }
    public int SortOrder { get; set; }

    /// <summary>True when the query modifies data — it commits on run and exports no file.</summary>
    public bool IsWriteQuery { get; set; }

    public string? KeyColumn { get; set; }
    public string? KeyParameter { get; set; }
    public string? InitialKey { get; set; }

    /// <summary>Saved checkpoint of the last successful run (read-only; reset via ResetKey on save).</summary>
    public string? LastKeyValue { get; set; }
}

public class ScheduledTaskViewerDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
}

public class ScheduledTaskRunDto
{
    public Guid Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? TriggeredByUsername { get; set; }
    public string? Error { get; set; }
    public List<ScheduledTaskItemResult> Items { get; set; } = new();
}

/// <summary>
/// Shared entity → DTO mapping for scheduled tasks. Callers must have loaded the
/// task with its Triggers, Items (incl. DynamicQuery) and Viewers (incl. User) navigations.
/// </summary>
public static class ScheduledTaskMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ScheduledTaskDto ToDto(ScheduledTask task, ScheduledTaskRun? lastRun = null) => new()
    {
        Id = task.Id,
        Name = task.Name,
        Description = task.Description ?? string.Empty,
        IsEnabled = task.IsEnabled,
        OutputFolder = task.OutputFolder,
        ArchiveFolder = task.ArchiveFolder,
        CombineOutput = task.CombineOutput,
        IncludeHeaders = task.IncludeHeaders,
        CombinedFileName = task.CombinedFileName,
        CombinedFormat = task.CombinedFormat,
        CombinedCsvSeparator = task.CombinedCsvSeparator,
        CombinedAppendTimestamp = task.CombinedAppendTimestamp,
        Triggers = task.Triggers
            .OrderBy(t => t.SortOrder)
            .Select(t => new ScheduledTaskTriggerDto
            {
                Frequency = t.Frequency,
                IntervalMinutes = t.IntervalMinutes,
                TimeOfDay = t.TimeOfDay,
                DayOfWeek = t.DayOfWeek,
                DayOfMonth = t.DayOfMonth,
                SortOrder = t.SortOrder
            })
            .ToList(),
        NextRunAt = task.NextRunAt,
        CreatedAt = task.CreatedAt,
        Items = task.Items
            .OrderBy(i => i.SortOrder)
            .Select(i => new ScheduledTaskItemDto
            {
                Id = i.Id,
                DynamicQueryId = i.DynamicQueryId,
                QueryName = i.DynamicQuery?.Name ?? string.Empty,
                Parameters = ParseParameters(i.ParametersJson),
                ExportFormat = i.ExportFormat,
                CsvSeparator = i.CsvSeparator,
                FileNamePrefix = i.FileNamePrefix,
                AppendTimestamp = i.AppendTimestamp,
                SortOrder = i.SortOrder,
                IsWriteQuery = IsWriteQuery(i.DynamicQuery?.SqlQuery),
                KeyColumn = i.KeyColumn,
                KeyParameter = i.KeyParameter,
                InitialKey = i.InitialKey,
                LastKeyValue = i.LastKeyValue
            })
            .ToList(),
        Viewers = task.Viewers
            .Select(v => new ScheduledTaskViewerDto
            {
                UserId = v.UserId,
                Username = v.User?.Username ?? string.Empty
            })
            .OrderBy(v => v.Username)
            .ToList(),
        LastRun = lastRun is null ? null : ToRunDto(lastRun)
    };

    public static ScheduledTaskRunDto ToRunDto(ScheduledTaskRun run) => new()
    {
        Id = run.Id,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        Status = run.Status.ToString(),
        TriggeredByUsername = run.TriggeredByUsername,
        Error = run.Error,
        Items = ParseItemResults(run.ItemResultsJson)
    };

    private static bool IsWriteQuery(string? sql)
    {
        var trimmed = sql?.TrimStart() ?? string.Empty;
        return trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseParameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static List<ScheduledTaskItemResult> ParseItemResults(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ScheduledTaskItemResult>();
        try
        {
            return JsonSerializer.Deserialize<List<ScheduledTaskItemResult>>(json, JsonOptions) ?? new();
        }
        catch (JsonException)
        {
            return new List<ScheduledTaskItemResult>();
        }
    }
}
