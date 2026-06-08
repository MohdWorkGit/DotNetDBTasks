using DotNetDBTasks.Application.Common.Interfaces;
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

    public UserQueriesController(
        IMediator mediator,
        IQueryJobStore jobStore,
        IQueryJobQueue jobQueue,
        ICurrentUserService currentUser)
    {
        _mediator = mediator;
        _jobStore = jobStore;
        _jobQueue = jobQueue;
        _currentUser = currentUser;
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
            Confirmed = request.Confirmed
        };
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
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
            Confirmed = request.Confirmed
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
    /// Returns the status of an async query job, including the result once it has succeeded
    /// or the error message if it failed/was canceled. Only the submitting user (or an Admin)
    /// may read a job.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}")]
    public IActionResult GetJob(Guid jobId)
    {
        var job = _jobStore.Get(jobId);
        if (job is null)
            return NotFound();

        if (job.UserId != _currentUser.UserId && !_currentUser.Roles.Contains("Admin"))
            return Forbid();

        return Ok(new
        {
            status = job.Status.ToString(),
            result = job.Status == QueryJobStatus.Succeeded ? job.Result : null,
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

}
