using Bayan.Domain.Constants;
using Bayan.API.Authorization;
using System.Text.Json;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;
using Bayan.Application.Features.DynamicQueries.Queries;
using Bayan.Application.Features.QueryExecution.Commands;
using Bayan.Application.Features.QueryExecution.Queries;
using Bayan.Application.Features.QueryGroups.Queries;
using Bayan.Domain.Enums;
using Bayan.Domain.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bayan.API.Controllers;

public class ExecuteQueryRequest
{
    public Dictionary<string, string>? Parameters { get; set; }
    public bool Confirmed { get; set; }
}

/// <summary>
/// User endpoints for viewing and executing assigned queries.
/// </summary>
[ApiController]
[Route("api/user/queries")]
// Reachable by either capability: this controller now serves the shared "My Queries" page,
// which lists reports beside queries, and the paging endpoint the report viewer reads its
// sections from. Every action that actually touches a query re-states queries.run below, so a
// report-only role reaches the page and its own cached rows and nothing else.
[RequirePermission(Permissions.QueriesRun, Permissions.ReportsRun)]
public class UserQueriesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IQueryJobStore _jobStore;
    private readonly IQueryJobQueue _jobQueue;
    private readonly ICurrentUserService _currentUser;
    private readonly IResultFileExporter _resultFileExporter;
    private readonly IDocxToPdfConverter _docxToPdfConverter;
    private readonly IPermissionService _permissions;

    public UserQueriesController(
        IMediator mediator,
        IQueryJobStore jobStore,
        IQueryJobQueue jobQueue,
        ICurrentUserService currentUser,
        IResultFileExporter resultFileExporter,
        IDocxToPdfConverter docxToPdfConverter,
        IPermissionService permissions)
    {
        _mediator = mediator;
        _jobStore = jobStore;
        _jobQueue = jobQueue;
        _currentUser = currentUser;
        _resultFileExporter = resultFileExporter;
        _docxToPdfConverter = docxToPdfConverter;
        _permissions = permissions;
    }

    /// <summary>
    /// Retrieves all queries available to the current user based on role assignments.
    /// </summary>
    [RequirePermission(Permissions.QueriesRun)]
    [HttpGet]
    public async Task<IActionResult> GetMyQueries(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetQueriesForUserQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the same accessible queries as <see cref="GetMyQueries"/> but bucketed
    /// into the QueryGroups they belong to (and an "Ungrouped" bucket).
    /// </summary>
    [HttpGet("groups")]
    public async Task<IActionResult> GetMyGroups(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyQueryGroupsQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves a single query by its identifier, verifying the current user has access.
    /// </summary>
    [RequirePermission(Permissions.QueriesRun)]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetMyQueryById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetQueryForUserByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Executes a dynamic query synchronously and returns the result in the same request.
    /// Used for normal, quick queries (those not flagged as long-running) so there is no
    /// polling overhead.
    /// </summary>
    /// <remarks>
    /// Read queries run once with no row cap; the full result set is cached server-side and this
    /// endpoint returns lightweight metadata plus a <c>jobId</c>. The grid then fetches pages via
    /// <see cref="GetJobRows"/> and Excel export reuses the same cached result via
    /// <see cref="ExportFile"/> — so the query executes only once for both. Write queries are
    /// returned inline (preview/confirm) exactly as before and are not cached.
    /// </remarks>
    [RequirePermission(Permissions.QueriesRun)]
    [HttpPost("{id:guid}/execute")]
    public async Task<IActionResult> Execute(
        Guid id,
        [FromBody] ExecuteQueryRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ExecuteQueryCommand
        {
            QueryId = id,
            Parameters = request.Parameters ?? new(),
            Confirmed = request.Confirmed,
            CacheFullResult = true
        };
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(BuildExecuteResponse(result, command));
    }

    /// <summary>
    /// Returns a single page of a completed read job's cached result, applying server-side column
    /// filtering and sorting over the full result set. The grid calls this on every page/sort/filter
    /// change so only one page of rows crosses the wire, while the underlying query ran just once.
    /// Only the submitting user (or an Admin) may read a job.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}/rows")]
    public IActionResult GetJobRows(
        Guid jobId,
        int pageIndex = 0,
        int pageSize = 25,
        string? sortColumn = null,
        string? sortDir = null,
        string? filters = null)
    {
        var job = _jobStore.Get(jobId);
        if (job is null)
            return JobGone();

        if (job.UserId != _currentUser.UserId && !_currentUser.Roles.Contains("Admin"))
            return Forbid();

        if (job.Status != QueryJobStatus.Succeeded || job.CachedRows is null)
            return Conflict(new { message = "The result is not ready." });

        // The cached result pages/filters/sorts internally over its heap or on-disk storage, so a
        // large spilled result never has to be materialized here.
        var page = job.CachedRows.GetPage(pageIndex, pageSize, sortColumn, sortDir, ParseFilters(filters));

        return Ok(new
        {
            rows = page.Rows,
            filteredTotal = page.FilteredTotal,
            totalRows = page.TotalRows
        });
    }

    /// <summary>
    /// Streams a finished read job's cached result as a downloadable file in the requested
    /// format — Excel (.xlsx, default), CSV, JSON, PDF or Word (.docx). The full result set
    /// (built with no row cap during execution) is converted on demand — no second query run.
    /// Word exports use the query's uploaded template when one exists, otherwise the built-in
    /// default layout. Only the submitting user (or an Admin) may download; the job must have
    /// succeeded.
    /// </summary>
    [RequirePermission(Permissions.QueriesRun)]
    [HttpGet("jobs/{jobId:guid}/export-file")]
    public async Task<IActionResult> ExportFile(
        Guid jobId,
        [FromQuery] string? format,
        CancellationToken cancellationToken)
    {
        var job = _jobStore.Get(jobId);
        if (job is null)
            return JobGone();

        if (job.UserId != _currentUser.UserId && !_currentUser.Roles.Contains("Admin"))
            return Forbid();

        if (job.Status != QueryJobStatus.Succeeded || job.Result is null || job.CachedRows is null)
            return Conflict(new { message = "The export is not ready." });

        var fileFormat = (format ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "xlsx" or "excel" => ExportFileFormat.Excel,
            "csv" => ExportFileFormat.Csv,
            "json" => ExportFileFormat.Json,
            "pdf" => ExportFileFormat.Pdf,
            "docx" or "word" => ExportFileFormat.Word,
            _ => (ExportFileFormat?)null
        };
        if (fileFormat is null)
            return BadRequest(new { message = $"Unsupported export format '{format}'. Use xlsx, csv, json, pdf or docx." });

        // Both gates, server-side. The client hides formats it cannot use, but hiding a menu
        // item is not a control — this endpoint is reachable directly with any format string.
        var exportedQuery = await _mediator.Send(
            new GetDynamicQueryByIdQuery(job.Command.QueryId), cancellationToken);

        // The DTO already exposes the list; re-parsing turns the names back into enum values.
        if (!ExportPermissions.Parse(string.Join(',', exportedQuery.AllowedExportFormats))
                .Contains(fileFormat.Value))
            return Forbid();

        if (!await _permissions.HasAsync(
                _currentUser.Roles, ExportPermissions.PermissionFor(fileFormat.Value), cancellationToken))
            return Forbid();

        byte[]? wordTemplate = null;
        var exportName = "Results";
        List<ExportParameter>? exportParameters = null;
        // PDF exports are rendered from the Word template too when a DOCX->PDF engine is
        // installed, so both formats need the effective template and the query name.
        var usesWordTemplate = fileFormat == ExportFileFormat.Word ||
            (fileFormat == ExportFileFormat.Pdf && _docxToPdfConverter.IsAvailable);
        if (usesWordTemplate)
        {
            exportName = exportedQuery.Name;
            // Per-query template, else the system default; null falls back to the built-in starter.
            wordTemplate = await _mediator.Send(
                new GetEffectiveWordTemplateQuery(job.Command.QueryId), cancellationToken);
            // Template can print each parameter ({{@name}}) and the {{PARAMS}} summary; the
            // display names come from the query definition, the values from the executed job.
            exportParameters = exportedQuery.Parameters
                .OrderBy(p => p.SortOrder)
                .Select(p => new ExportParameter(
                    p.Name, p.DisplayName, job.Result.Parameters.GetValueOrDefault(p.Name)))
                .ToList();
        }

        // AsRowList() streams from disk for a spilled result, so a large export is not re-materialized.
        var bytes = _resultFileExporter.Export(
            fileFormat.Value, job.CachedRows.Columns, job.CachedRows.AsRowList(), exportName,
            wordTemplate: wordTemplate, parameters: exportParameters);
        var extension = _resultFileExporter.GetExtension(fileFormat.Value);
        var contentType = fileFormat.Value switch
        {
            ExportFileFormat.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ExportFileFormat.Csv => "text/csv",
            ExportFileFormat.Json => "application/json",
            ExportFileFormat.Pdf => "application/pdf",
            ExportFileFormat.Word => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };
        var fileName = $"query-export-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}";
        return File(bytes, contentType, fileName);
    }

    /// <summary>
    /// Submits a dynamic query for asynchronous execution and returns a job id immediately.
    /// The query runs on a background worker; the client polls <see cref="GetJob"/> for the
    /// result. Used for queries flagged as long-running so every HTTP request stays short and
    /// is not killed by proxy/edge timeouts (nginx, Cloudflare, IIS).
    /// </summary>
    [RequirePermission(Permissions.QueriesRun)]
    [HttpPost("{id:guid}/execute-async")]
    public async Task<IActionResult> ExecuteAsync(
        Guid id,
        [FromBody] ExecuteQueryRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ExecuteQueryCommand
        {
            QueryId = id,
            Parameters = request.Parameters ?? new(),
            Confirmed = request.Confirmed,
            CacheFullResult = true
        };

        var snapshot = new UserContextSnapshot(
            _currentUser.UserId,
            _currentUser.Username,
            _currentUser.Roles);

        var job = _jobStore.Create(_currentUser.UserId, snapshot, command);
        await _jobQueue.EnqueueAsync(job.Id, cancellationToken);

        return Accepted(new { jobId = job.Id });
    }

    /// <summary>
    /// Returns the status of an async query job. Once succeeded, it returns lightweight execution
    /// metadata (columns, row count, and the jobId for paging/export) for a read result, or the
    /// full inline result for a write preview/confirmation. The potentially huge read rows are
    /// never sent here — the grid fetches them a page at a time from <see cref="GetJobRows"/>.
    /// Only the submitting user (or an Admin) may read a job.
    /// </summary>
    [RequirePermission(Permissions.QueriesRun)]
    [HttpGet("jobs/{jobId:guid}")]
    public IActionResult GetJob(Guid jobId)
    {
        var job = _jobStore.Get(jobId);
        if (job is null)
            return JobGone();

        if (job.UserId != _currentUser.UserId && !_currentUser.Roles.Contains("Admin"))
            return Forbid();

        object? payload = null;
        if (job.Status == QueryJobStatus.Succeeded && job.Result is not null)
            payload = ToExecuteDto(job.Result, job.Id);

        return Ok(new
        {
            status = job.Status.ToString(),
            result = payload,
            error = job.Error
        });
    }

    /// <summary>
    /// Cancels a running async query job, stopping the underlying database command. Only the
    /// submitting user (or an Admin) may cancel a job.
    /// </summary>
    [RequirePermission(Permissions.QueriesRun)]
    [HttpPost("jobs/{jobId:guid}/cancel")]
    public IActionResult CancelJob(Guid jobId)
    {
        var job = _jobStore.Get(jobId);
        if (job is null)
            return NotFound();

        if (job.UserId != _currentUser.UserId && !_currentUser.Roles.Contains("Admin"))
            return Forbid();

        _jobStore.Cancel(jobId);
        return NoContent();
    }

    /// <summary>
    /// Releases a cached result immediately, freeing its memory. The client calls this when it
    /// leaves the results page; otherwise the job expires on its own after the sliding retention
    /// window. Only the submitting user (or an Admin) may release a job. Idempotent.
    /// </summary>
    [HttpDelete("jobs/{jobId:guid}")]
    public IActionResult ReleaseJob(Guid jobId)
    {
        var job = _jobStore.Get(jobId);
        if (job is null)
            return NoContent();

        if (job.UserId != _currentUser.UserId && !_currentUser.Roles.Contains("Admin"))
            return Forbid();

        _jobStore.Remove(jobId);
        return NoContent();
    }

    /// <summary>
    /// Returns the selectable options for a dropdown parameter.
    /// Options are either the static list defined by the admin or the result of a lookup query.
    /// </summary>
    [RequirePermission(Permissions.QueriesRun)]
    [HttpGet("{queryId:guid}/parameters/{parameterId:guid}/dropdown-options")]
    public async Task<IActionResult> GetDropdownOptions(
        Guid queryId,
        Guid parameterId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetParameterDropdownOptionsQuery { QueryId = queryId, ParameterId = parameterId },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves one page of the current user's query execution history.
    /// </summary>
    [RequirePermission(Permissions.QueriesRun)]
    [HttpGet("history")]
    public async Task<IActionResult> GetMyHistory(
        [FromQuery] string? sortBy,
        [FromQuery] bool sortDescending = true,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetMyExecutionHistoryQuery
            {
                SortBy = sortBy,
                SortDescending = sortDescending,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves one page of the pre-change row snapshots recorded for one of the current
    /// user's execution logs.
    /// </summary>
    [RequirePermission(Permissions.QueriesRun)]
    [HttpGet("history/{id:guid}/old-values")]
    public async Task<IActionResult> GetMyHistoryOldValues(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetExecutionLogOldValuesQuery
            {
                LogId = id,
                PageNumber = pageNumber,
                PageSize = pageSize,
                RestrictToCurrentUser = true
            },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Caches a read result server-side (so the grid can page it and export can reuse it) and
    /// returns the metadata envelope. Write/preview results are small and returned inline without
    /// caching, so the confirm/commit flow is unchanged.
    /// </summary>
    private object BuildExecuteResponse(QueryExecutionResult result, ExecuteQueryCommand command)
    {
        var isReadResult = !result.RequiresConfirmation && result.Columns.Count > 0;
        if (!isReadResult)
            return ToExecuteDto(result, null);

        var snapshot = new UserContextSnapshot(
            _currentUser.UserId,
            _currentUser.Username,
            _currentUser.Roles);

        var job = _jobStore.Create(_currentUser.UserId, snapshot, command);
        // Store caches the rows (heap or disk) and marks the job succeeded.
        _jobStore.SetResult(job.Id, result);

        return ToExecuteDto(result, job.Id);
    }

    /// <summary>
    /// Lightweight execution metadata sent to the client. Deliberately omits the read <c>Rows</c>
    /// (fetched a page at a time via <see cref="GetJobRows"/>); <paramref name="jobId"/> is set only
    /// for cached read results so the client can page and export them.
    /// </summary>
    private static object ToExecuteDto(QueryExecutionResult result, Guid? jobId)
    {
        var isReadResult = !result.RequiresConfirmation && result.Columns.Count > 0;
        return new
        {
            jobId = isReadResult ? jobId : null,
            requiresConfirmation = result.RequiresConfirmation,
            columns = result.Columns,
            totalRows = result.TotalRows,
            affectedRows = result.AffectedRows,
            isLimitReached = result.IsLimitReached,
            executionDurationMs = result.ExecutionDurationMs,
            previewColumns = result.PreviewColumns,
            previewRows = result.PreviewRows
        };
    }

    private static Dictionary<string, string> ParseFilters(string? filters)
    {
        if (string.IsNullOrWhiteSpace(filters))
            return new Dictionary<string, string>();

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(filters);
            return parsed is null
                ? new Dictionary<string, string>()
                : parsed
                    .Where(kv => !string.IsNullOrEmpty(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// The job id is not in the store. Deliberately 410 rather than 404: the job store is
    /// in-memory, so this is what a client sees after the result expired on its retention
    /// window or the worker process restarted out from under an in-flight query. A bare 404
    /// left the polling grid showing a generic "execution failed", which sent people hunting
    /// for a problem with their query. 410 plus a message lets the client say what actually
    /// happened and that re-running is the fix.
    /// </summary>
    private IActionResult JobGone() => StatusCode(
        StatusCodes.Status410Gone,
        new { message = "This result is no longer available. The server restarted or the result expired — please run the query again." });
}
