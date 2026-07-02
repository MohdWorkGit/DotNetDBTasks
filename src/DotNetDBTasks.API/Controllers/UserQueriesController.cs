using System.Text.Json;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Application.Features.QueryExecution.Commands;
using DotNetDBTasks.Application.Features.QueryExecution.Queries;
using DotNetDBTasks.Application.Features.QueryGroups.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

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
[Authorize]
public class UserQueriesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IQueryJobStore _jobStore;
    private readonly IQueryJobQueue _jobQueue;
    private readonly ICurrentUserService _currentUser;
    private readonly IExcelExporter _excelExporter;

    public UserQueriesController(
        IMediator mediator,
        IQueryJobStore jobStore,
        IQueryJobQueue jobQueue,
        ICurrentUserService currentUser,
        IExcelExporter excelExporter)
    {
        _mediator = mediator;
        _jobStore = jobStore;
        _jobQueue = jobQueue;
        _currentUser = currentUser;
        _excelExporter = excelExporter;
    }

    /// <summary>
    /// Retrieves all queries available to the current user based on role assignments.
    /// </summary>
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
            return NotFound();

        if (job.UserId != _currentUser.UserId && !_currentUser.Roles.Contains("Admin"))
            return Forbid();

        if (job.Status != QueryJobStatus.Succeeded || job.Result is null)
            return Conflict(new { message = "The result is not ready." });

        var result = job.Result;
        IEnumerable<Dictionary<string, object?>> rows = result.Rows;

        // Per-column "contains" filter (case-insensitive) — mirrors the previous client-side filter.
        var parsedFilters = ParseFilters(filters);
        foreach (var (col, term) in parsedFilters)
        {
            var needle = term;
            rows = rows.Where(r =>
                r.TryGetValue(col, out var v) &&
                (v?.ToString() ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = rows.ToList();
        var filteredTotal = filtered.Count;

        if (!string.IsNullOrWhiteSpace(sortColumn) && result.Columns.Contains(sortColumn))
        {
            var direction = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
            filtered.Sort((a, b) =>
                CompareCells(a.GetValueOrDefault(sortColumn!), b.GetValueOrDefault(sortColumn!)) * direction);
        }

        if (pageSize <= 0) pageSize = 25;
        if (pageIndex < 0) pageIndex = 0;

        var pageRows = filtered.Skip(pageIndex * pageSize).Take(pageSize).ToList();

        return Ok(new
        {
            rows = pageRows,
            filteredTotal,
            totalRows = result.TotalRows
        });
    }

    /// <summary>
    /// Streams a finished read job's cached result as an Excel (.xlsx) file. The full result set
    /// (built with no row cap during execution) is converted to a workbook on demand — no second
    /// query run. Only the submitting user (or an Admin) may download; the job must have succeeded.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}/export-file")]
    public IActionResult ExportFile(Guid jobId)
    {
        var job = _jobStore.Get(jobId);
        if (job is null)
            return NotFound();

        if (job.UserId != _currentUser.UserId && !_currentUser.Roles.Contains("Admin"))
            return Forbid();

        if (job.Status != QueryJobStatus.Succeeded || job.Result is null)
            return Conflict(new { message = "The export is not ready." });

        var bytes = _excelExporter.Export(job.Result.Columns, job.Result.Rows, "Results");
        var fileName = $"query-export-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx";
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    /// <summary>
    /// Submits a dynamic query for asynchronous execution and returns a job id immediately.
    /// The query runs on a background worker; the client polls <see cref="GetJob"/> for the
    /// result. Used for queries flagged as long-running so every HTTP request stays short and
    /// is not killed by proxy/edge timeouts (nginx, Cloudflare, IIS).
    /// </summary>
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
            _currentUser.Department,
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
    [HttpGet("jobs/{jobId:guid}")]
    public IActionResult GetJob(Guid jobId)
    {
        var job = _jobStore.Get(jobId);
        if (job is null)
            return NotFound();

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
    /// Retrieves the current user's query execution history.
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetMyHistory(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyExecutionHistoryQuery(), cancellationToken);
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
            _currentUser.Department,
            _currentUser.Roles);

        var job = _jobStore.Create(_currentUser.UserId, snapshot, command);
        _jobStore.Update(job.Id, j =>
        {
            j.Result = result;
            j.Status = QueryJobStatus.Succeeded;
        });

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
    /// Orders two cell values: same-typed comparables compare directly, otherwise fall back to a
    /// numeric compare when both parse as numbers, and finally to a case-insensitive string compare.
    /// Nulls sort first.
    /// </summary>
    private static int CompareCells(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        if (a.GetType() == b.GetType() && a is IComparable comparable)
            return comparable.CompareTo(b);

        if (decimal.TryParse(a.ToString(), out var da) && decimal.TryParse(b.ToString(), out var db))
            return da.CompareTo(db);

        return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

}
