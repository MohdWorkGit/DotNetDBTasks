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
    private readonly IScheduledTaskRunRegistry _runRegistry;
    private readonly ILogger<ScheduledTaskRunner> _logger;

    /// <summary>
    /// System default Word template for the current run, used when a Word-exporting item's
    /// query has no template of its own (and for combined Word output). Loaded once per run.
    /// </summary>
    private byte[]? _defaultWordTemplate;

    public ScheduledTaskRunner(
        IUnitOfWork unitOfWork,
        IMediator mediator,
        IUserExecutionContext userContext,
        IResultFileExporter exporter,
        IScheduledTaskRunRegistry runRegistry,
        ILogger<ScheduledTaskRunner> logger)
    {
        _unitOfWork = unitOfWork;
        _mediator = mediator;
        _userContext = userContext;
        _exporter = exporter;
        _runRegistry = runRegistry;
        _logger = logger;
    }

    public async Task RunAsync(ScheduledTaskRunRequest request, CancellationToken cancellationToken)
    {
        var task = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == request.ScheduledTaskId, cancellationToken,
            "Items", "Items.DynamicQuery", "Items.DynamicQuery.Parameters"))
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

        // Register the run so an admin can cancel it; the token also trips on app shutdown. Running
        // under runToken means a cancel aborts the executing DB command, not just the API await.
        var runCts = _runRegistry.Register(run.Id, cancellationToken);
        var runToken = runCts.Token;

        var results = new List<ScheduledTaskItemResult>();
        try
        {
            // Runs execute under the task creator's identity so the unchanged query
            // pipeline applies their access rights and attributes the audit log to them.
            _userContext.Current = await BuildCreatorSnapshotAsync(task.CreatedByUserId, runToken);

            Directory.CreateDirectory(task.OutputFolder);
            if (!string.IsNullOrWhiteSpace(task.ArchiveFolder))
                Directory.CreateDirectory(task.ArchiveFolder);

            var orderedItems = task.Items.OrderBy(i => i.SortOrder).ToList();
            _defaultWordTemplate = await LoadDefaultWordTemplateAsync(task, orderedItems, runToken);
            if (task.CombineOutput)
            {
                results.AddRange(await RunCombinedAsync(task, orderedItems, runToken));
            }
            else
            {
                foreach (var item in orderedItems)
                    results.Add(await RunItemAsync(task, item, runToken));
            }

            run.Status = results.All(r => r.Success) ? ScheduledTaskRunStatus.Succeeded
                : results.Any(r => r.Success) ? ScheduledTaskRunStatus.PartiallySucceeded
                : ScheduledTaskRunStatus.Failed;
        }
        catch (Exception) when (runCts.IsCancellationRequested)
        {
            // Any exception once cancellation was requested is a consequence of the cancel — some DB
            // providers surface the aborted command as a provider error (e.g. Oracle ORA-01013)
            // rather than OperationCanceledException.
            _logger.LogInformation(
                "Scheduled task {TaskName} ({TaskId}) run was canceled", task.Name, task.Id);
            run.Status = ScheduledTaskRunStatus.Canceled;
            run.Error = "Run was canceled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled task {TaskName} ({TaskId}) failed", task.Name, task.Id);
            run.Status = ScheduledTaskRunStatus.Failed;
            run.Error = Truncate(ex.Message, 2000);
        }
        finally
        {
            _runRegistry.Unregister(run.Id);
            _userContext.Current = null;
            run.CompletedAt = DateTime.UtcNow;
            run.ItemResultsJson = JsonSerializer.Serialize(results, JsonOptions);
            _unitOfWork.ScheduledTaskRuns.Update(run);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<byte[]?> LoadDefaultWordTemplateAsync(
        ScheduledTask task,
        IReadOnlyList<ScheduledTaskItem> items,
        CancellationToken cancellationToken)
    {
        // PDF counts too: with a DOCX->PDF engine installed, PDF exports are rendered
        // from the same Word template (the exporter ignores it otherwise).
        static bool UsesTemplate(ExportFileFormat f) => f is ExportFileFormat.Word or ExportFileFormat.Pdf;
        var anyWord = (task.CombineOutput && UsesTemplate(task.CombinedFormat))
            || (!task.CombineOutput && items.Any(i => UsesTemplate(i.ExportFormat)));
        if (!anyWord)
            return null;

        var stored = (await _unitOfWork.SystemTemplates.FindAsync(
            t => t.Key == SystemTemplate.WordDefaultKey, cancellationToken)).FirstOrDefault();
        return stored?.Content;
    }

    private async Task<ScheduledTaskItemResult> RunItemAsync(
        ScheduledTask task,
        ScheduledTaskItem item,
        CancellationToken cancellationToken)
    {
        var queryName = item.DynamicQuery?.Name ?? item.DynamicQueryId.ToString();
        var isWrite = item.DynamicQuery?.QueryType.IsWrite() ?? false;
        var result = new ScheduledTaskItemResult { QueryName = queryName, IsWrite = isWrite };
        var sw = Stopwatch.StartNew();
        try
        {
            var execution = await ExecuteItemQueryAsync(item, isWrite, cancellationToken);

            if (isWrite)
            {
                // Write queries produce no file — only the affected-row count is recorded.
                result.RowCount = execution.AffectedRows;
            }
            else
            {
                var bytes = _exporter.Export(
                    item.ExportFormat, execution.Columns, execution.Rows, queryName,
                    CsvSeparator.Parse(item.CsvSeparator), task.IncludeHeaders,
                    item.DynamicQuery?.WordTemplate ?? _defaultWordTemplate,
                    BuildExportParameters(item.DynamicQuery, execution.Parameters));
                var fileName = BuildFileName(task, item, queryName);
                await WriteOutputAsync(task, fileName, bytes, cancellationToken);

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
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw; // a run cancellation must stop the whole run, not be isolated as an item failure
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
    /// Combined mode: write queries commit as usual, and every read query's rows are
    /// collected and written into ONE file in item order — the same behaviour as the
    /// standalone QueryRunner. Read items only succeed (and only advance their
    /// checkpoints) once the combined file has been written, so a failed write means
    /// the next run re-exports everything.
    /// </summary>
    private async Task<List<ScheduledTaskItemResult>> RunCombinedAsync(
        ScheduledTask task,
        IReadOnlyList<ScheduledTaskItem> items,
        CancellationToken cancellationToken)
    {
        var results = new List<ScheduledTaskItemResult>();
        var collected = new List<(ScheduledTaskItem Item, ScheduledTaskItemResult Result, ExportResultSet Set, string? NextKey)>();

        foreach (var item in items)
        {
            var queryName = item.DynamicQuery?.Name ?? item.DynamicQueryId.ToString();
            var isWrite = item.DynamicQuery?.QueryType.IsWrite() ?? false;
            var result = new ScheduledTaskItemResult { QueryName = queryName, IsWrite = isWrite };
            results.Add(result);
            var sw = Stopwatch.StartNew();
            try
            {
                var execution = await ExecuteItemQueryAsync(item, isWrite, cancellationToken);
                if (isWrite)
                {
                    result.RowCount = execution.AffectedRows;
                    result.Success = true;
                }
                else
                {
                    result.RowCount = execution.TotalRows;
                    collected.Add((item, result,
                        new ExportResultSet(execution.Columns, execution.Rows),
                        ExtractNextKey(item, execution)));
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw; // a run cancellation must stop the whole run, not be isolated as an item failure
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
        }

        if (collected.Count == 0)
            return results;

        try
        {
            var bytes = _exporter.Export(
                task.CombinedFormat,
                collected.Select(c => c.Set).ToList(),
                string.IsNullOrWhiteSpace(task.CombinedFileName) ? task.Name : task.CombinedFileName,
                CsvSeparator.Parse(task.CombinedCsvSeparator),
                task.IncludeHeaders,
                // Combined output merges several queries, so only the system default applies.
                _defaultWordTemplate);
            var fileName = BuildCombinedFileName(task);
            await WriteOutputAsync(task, fileName, bytes, cancellationToken);

            foreach (var (item, result, _, nextKey) in collected)
            {
                result.FileName = fileName;
                result.Success = true;
                if (nextKey is not null)
                {
                    item.LastKeyValue = nextKey;
                    _unitOfWork.ScheduledTaskItems.Update(item);
                    result.LastKey = nextKey;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Scheduled task {TaskName}: writing the combined output file failed", task.Name);
            foreach (var (_, result, _, _) in collected)
                result.Error = Truncate("Combined output file could not be written: " + ex.Message, 2000);
        }

        return results;
    }

    /// <summary>
    /// Runs the item's query through the regular pipeline. Reads run Unlimited so
    /// exports contain the complete result set; writes run Confirmed so the change
    /// actually commits (the interactive preview/confirm handshake has no place in
    /// an unattended scheduled run).
    /// </summary>
    private async Task<QueryExecutionResult> ExecuteItemQueryAsync(
        ScheduledTaskItem item,
        bool isWrite,
        CancellationToken cancellationToken)
    {
        var parameters = string.IsNullOrWhiteSpace(item.ParametersJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(item.ParametersJson) ?? new();

        // Incremental read item: the saved checkpoint (or the initial key before the
        // first run) is injected as the configured query parameter's value.
        if (!isWrite && !string.IsNullOrWhiteSpace(item.KeyColumn) && !string.IsNullOrWhiteSpace(item.KeyParameter))
            parameters[item.KeyParameter] = item.LastKeyValue ?? item.InitialKey ?? string.Empty;

        return await _mediator.Send(new ExecuteQueryCommand
        {
            QueryId = item.DynamicQueryId,
            Parameters = parameters,
            Unlimited = !isWrite,
            Confirmed = isWrite
        }, cancellationToken);
    }

    /// <summary>
    /// Pairs each of the query's defined parameters (for name/display name and order) with the
    /// value it ran with, so a Word/PDF template can print {{@name}} and the {{PARAMS}} summary.
    /// </summary>
    private static IReadOnlyList<ExportParameter>? BuildExportParameters(
        DynamicQuery? query,
        IReadOnlyDictionary<string, object?> values)
    {
        if (query is null || query.Parameters.Count == 0)
            return null;

        return query.Parameters
            .OrderBy(p => p.SortOrder)
            .Select(p => new ExportParameter(p.Name, p.DisplayName, values.GetValueOrDefault(p.Name)))
            .ToList();
    }

    /// <summary>
    /// Writes the file into the task's output folder and, when configured, the archive
    /// folder. A failed archive copy fails the item (and keeps its checkpoint), so the
    /// next run re-exports rather than silently leaving a gap there.
    /// </summary>
    private static async Task WriteOutputAsync(
        ScheduledTask task,
        string fileName,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        await File.WriteAllBytesAsync(Path.Combine(task.OutputFolder, fileName), bytes, cancellationToken);
        if (!string.IsNullOrWhiteSpace(task.ArchiveFolder))
            await File.WriteAllBytesAsync(Path.Combine(task.ArchiveFolder, fileName), bytes, cancellationToken);
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

    private const string DefaultTimestampFormat = "_yyyyMMdd-HHmmss";

    private string BuildFileName(ScheduledTask task, ScheduledTaskItem item, string queryName)
    {
        var baseName = SanitizeFileName(
            string.IsNullOrWhiteSpace(item.FileNamePrefix) ? queryName : item.FileNamePrefix);
        if (item.AppendTimestamp)
            baseName += FormatTimestamp(task);
        return baseName + "." + _exporter.GetExtension(item.ExportFormat);
    }

    private string BuildCombinedFileName(ScheduledTask task)
    {
        var baseName = SanitizeFileName(
            string.IsNullOrWhiteSpace(task.CombinedFileName) ? task.Name : task.CombinedFileName);
        if (task.CombinedAppendTimestamp)
            baseName += FormatTimestamp(task);
        return baseName + "." + _exporter.GetExtension(task.CombinedFormat);
    }

    private static string FormatTimestamp(ScheduledTask task) =>
        DateTime.Now.ToString(
            string.IsNullOrWhiteSpace(task.TimestampFormat) ? DefaultTimestampFormat : task.TimestampFormat,
            CultureInfo.InvariantCulture);

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
