using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Application.Features.QueryExecution.Commands;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DotNetDBTasks.Infrastructure.Services;

/// <summary>
/// Executes a scheduled task: each query item is replayed through the regular
/// <see cref="ExecuteQueryCommand"/> pipeline under the task creator's identity (so
/// access checks, database-user resolution and audit logging behave exactly like an
/// interactive run), then the full result set is exported to a file in the task's
/// output folder. Item failures are isolated — one bad query never stops the rest.
/// </summary>
public class ScheduledTaskRunner : IScheduledTaskRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;
    private readonly IUserExecutionContext _userContext;
    private readonly IResultFileExporter _exporter;
    private readonly ILogger<ScheduledTaskRunner> _logger;

    public ScheduledTaskRunner(
        IUnitOfWork unitOfWork,
        IMediator mediator,
        IUserExecutionContext userContext,
        IResultFileExporter exporter,
        ILogger<ScheduledTaskRunner> logger)
    {
        _unitOfWork = unitOfWork;
        _mediator = mediator;
        _userContext = userContext;
        _exporter = exporter;
        _logger = logger;
    }

    public async Task RunAsync(ScheduledTaskRunRequest request, CancellationToken cancellationToken)
    {
        var task = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == request.ScheduledTaskId, cancellationToken, "Items", "Items.DynamicQuery"))
            .FirstOrDefault();
        if (task is null)
        {
            _logger.LogWarning("Scheduled task {TaskId} no longer exists; skipping run", request.ScheduledTaskId);
            return;
        }

        var run = new ScheduledTaskRun
        {
            Id = Guid.NewGuid(),
            ScheduledTaskId = task.Id,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Status = ScheduledTaskRunStatus.Running,
            TriggeredByUserId = request.TriggeredByUserId,
            TriggeredByUsername = request.TriggeredByUsername
        };
        await _unitOfWork.ScheduledTaskRuns.AddAsync(run, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var results = new List<ScheduledTaskItemResult>();
        try
        {
            // Runs execute under the task creator's identity so the unchanged query
            // pipeline applies their access rights and attributes the audit log to them.
            _userContext.Current = await BuildCreatorSnapshotAsync(task.CreatedByUserId, cancellationToken);

            Directory.CreateDirectory(task.OutputFolder);

            foreach (var item in task.Items.OrderBy(i => i.SortOrder))
                results.Add(await RunItemAsync(task, item, cancellationToken));

            run.Status = results.All(r => r.Success) ? ScheduledTaskRunStatus.Succeeded
                : results.Any(r => r.Success) ? ScheduledTaskRunStatus.PartiallySucceeded
                : ScheduledTaskRunStatus.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled task {TaskName} ({TaskId}) failed", task.Name, task.Id);
            run.Status = ScheduledTaskRunStatus.Failed;
            run.Error = Truncate(ex.Message, 2000);
        }
        finally
        {
            _userContext.Current = null;
            run.CompletedAt = DateTime.UtcNow;
            run.ItemResultsJson = JsonSerializer.Serialize(results, JsonOptions);
            _unitOfWork.ScheduledTaskRuns.Update(run);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<ScheduledTaskItemResult> RunItemAsync(
        ScheduledTask task,
        ScheduledTaskItem item,
        CancellationToken cancellationToken)
    {
        var queryName = item.DynamicQuery?.Name ?? item.DynamicQueryId.ToString();
        var isWrite = IsWriteQuery(item.DynamicQuery?.SqlQuery ?? string.Empty);
        var result = new ScheduledTaskItemResult { QueryName = queryName, IsWrite = isWrite };
        var sw = Stopwatch.StartNew();
        try
        {
            var parameters = string.IsNullOrWhiteSpace(item.ParametersJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(item.ParametersJson) ?? new();

            // Incremental read item: the saved checkpoint (or the initial key before the
            // first run) is injected as the configured query parameter's value.
            if (!isWrite && !string.IsNullOrWhiteSpace(item.KeyColumn) && !string.IsNullOrWhiteSpace(item.KeyParameter))
                parameters[item.KeyParameter] = item.LastKeyValue ?? item.InitialKey ?? string.Empty;

            // Reads run Unlimited so exports contain the complete result set. Writes run
            // Confirmed so the change actually commits (the interactive preview/confirm
            // handshake has no place in an unattended scheduled run).
            var execution = await _mediator.Send(new ExecuteQueryCommand
            {
                QueryId = item.DynamicQueryId,
                Parameters = parameters,
                Unlimited = !isWrite,
                Confirmed = isWrite
            }, cancellationToken);

            if (isWrite)
            {
                // Write queries produce no file — only the affected-row count is recorded.
                result.RowCount = execution.AffectedRows;
            }
            else
            {
                var bytes = _exporter.Export(item.ExportFormat, execution.Columns, execution.Rows, queryName);
                var fileName = BuildFileName(item, queryName);
                await File.WriteAllBytesAsync(Path.Combine(task.OutputFolder, fileName), bytes, cancellationToken);

                result.FileName = fileName;
                result.RowCount = execution.TotalRows;

                // Advance the checkpoint to the last returned row's key. The query should
                // ORDER BY the key ascending; no rows keeps the previous checkpoint.
                var nextKey = ExtractNextKey(item, execution);
                if (nextKey is not null)
                {
                    item.LastKeyValue = nextKey;
                    _unitOfWork.ScheduledTaskItems.Update(item);
                    result.LastKey = nextKey;
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Scheduled task {TaskName}: item {QueryName} failed", task.Name, queryName);
            result.Error = Truncate(ex.Message, 2000);
        }
        finally
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
        }
        return result;
    }

    /// <summary>
    /// The new checkpoint is the KeyColumn value of the last returned row, formatted
    /// culture-invariantly. Null when the item is not incremental, no rows came back,
    /// or the key value itself is NULL — all of which keep the previous checkpoint.
    /// </summary>
    private static string? ExtractNextKey(ScheduledTaskItem item, QueryExecutionResult execution)
    {
        if (string.IsNullOrWhiteSpace(item.KeyColumn) || execution.Rows.Count == 0)
            return null;

        var column = execution.Columns.FirstOrDefault(
            c => string.Equals(c, item.KeyColumn, StringComparison.OrdinalIgnoreCase));
        if (column is null)
            return null;

        execution.Rows[^1].TryGetValue(column, out var value);
        return value switch
        {
            null or DBNull => null,
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private static bool IsWriteQuery(string sql)
    {
        var trimmed = sql.TrimStart();
        return trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildFileName(ScheduledTaskItem item, string queryName)
    {
        var baseName = SanitizeFileName(
            string.IsNullOrWhiteSpace(item.FileNamePrefix) ? queryName : item.FileNamePrefix);
        if (item.AppendTimestamp)
            baseName += DateTime.Now.ToString("_yyyyMMdd-HHmmss");
        return baseName + "." + _exporter.GetExtension(item.ExportFormat);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned;
    }

    /// <summary>
    /// Reconstructs the creator's identity (roles/department) from the database so
    /// scheduled runs honor their *current* permissions, not those at creation time.
    /// </summary>
    private async Task<UserContextSnapshot> BuildCreatorSnapshotAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("The user who created this scheduled task no longer exists.");

        var userRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == userId, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToHashSet();
        var roles = (await _unitOfWork.Roles.FindAsync(r => roleIds.Contains(r.Id), cancellationToken))
            .Select(r => r.Name)
            .ToList();

        return new UserContextSnapshot(user.Id, user.Username, user.Department, roles);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
