using DotNetDBTasks.Application.Features.ScheduledTasks.Commands;
using DotNetDBTasks.Application.Features.ScheduledTasks.Queries;
using MediatR;
using DotNetDBTasks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Scheduled export tasks. Admins manage and trigger tasks; read endpoints are open
/// to all authenticated users but the handlers only return tasks the caller may see
/// (Admin/Auditor: all; others: tasks they were granted viewer permission on).
/// </summary>
[ApiController]
[Route("api/scheduledtasks")]
[Authorize]
public class ScheduledTasksController : ControllerBase
{
    private readonly IMediator _mediator;

    public ScheduledTasksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetScheduledTasksQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetScheduledTaskByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/runs")]
    public async Task<IActionResult> GetRuns(Guid id, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetScheduledTaskRunsQuery(id, take), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Downloads an export file recorded in a run's item results, served from the
    /// task's output folder (or the archive copy). 404 when the file no longer
    /// exists on the server.
    /// </summary>
    [HttpGet("{id:guid}/runs/{runId:guid}/file")]
    public async Task<IActionResult> DownloadRunFile(
        Guid id,
        Guid runId,
        [FromQuery] string fileName,
        CancellationToken cancellationToken)
    {
        var file = await _mediator.Send(
            new DownloadScheduledTaskRunFileQuery(id, runId, fileName), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Create(
        [FromBody] CreateScheduledTaskCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateScheduledTaskCommand command,
        CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteScheduledTaskCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Queues an immediate run of the task; the outcome appears in its run history.</summary>
    [HttpPost("{id:guid}/run")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> RunNow(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RunScheduledTaskNowCommand(id), cancellationToken);
        return Accepted();
    }

    /// <summary>
    /// Cancels a run that is currently executing, aborting its running query. 409 when the run is
    /// not in progress (already finished, or not running on this instance).
    /// </summary>
    [HttpPost("{id:guid}/runs/{runId:guid}/cancel")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> CancelRun(Guid id, Guid runId, CancellationToken cancellationToken)
    {
        var canceled = await _mediator.Send(new CancelScheduledTaskRunCommand(id, runId), cancellationToken);
        return canceled
            ? Accepted()
            : Conflict(new { message = "That run is not currently in progress." });
    }
}
