using Bayan.API.Authorization;
using Bayan.Application.Features.ScheduledTasks.Commands;
using Bayan.Application.Features.ScheduledTasks.Queries;
using MediatR;
using Bayan.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bayan.API.Controllers;

/// <summary>
/// Scheduled export tasks. Admins manage and trigger tasks; read endpoints are open
/// to all authenticated users but the handlers only return tasks the caller may see
/// (holders of <c>scheduledTasks.viewAll</c>: all; others: the tasks a viewer grant
/// reaches them on, by role, user group, or name).
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
    [RequirePermission(Permissions.ScheduledTasksManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreateScheduledTaskCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.ScheduledTasksManage)]
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
    [RequirePermission(Permissions.ScheduledTasksManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteScheduledTaskCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// The task's viewer grants, for the access page. Behind <c>scheduledTasks.manage</c>:
    /// who may see a task is not itself something a viewer of that task gets to read.
    /// </summary>
    [HttpGet("{id:guid}/access")]
    [RequirePermission(Permissions.ScheduledTasksManage)]
    public async Task<IActionResult> GetAccess(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetScheduledTaskAccessQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>Replaces the roles granted viewer access to this task.</summary>
    [HttpPut("{id:guid}/access/roles")]
    [RequirePermission(Permissions.ScheduledTasksManage)]
    public async Task<IActionResult> AssignAccessRoles(
        Guid id,
        [FromBody] AssignScheduledTaskRolesCommand command,
        CancellationToken cancellationToken)
    {
        command.TaskId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>Replaces the user groups granted viewer access to this task.</summary>
    [HttpPut("{id:guid}/access/user-groups")]
    [RequirePermission(Permissions.ScheduledTasksManage)]
    public async Task<IActionResult> AssignAccessUserGroups(
        Guid id,
        [FromBody] AssignScheduledTaskUserGroupsCommand command,
        CancellationToken cancellationToken)
    {
        command.TaskId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>Replaces the individually named users granted viewer access to this task.</summary>
    [HttpPut("{id:guid}/access/users")]
    [RequirePermission(Permissions.ScheduledTasksManage)]
    public async Task<IActionResult> AssignAccessUsers(
        Guid id,
        [FromBody] AssignScheduledTaskUsersCommand command,
        CancellationToken cancellationToken)
    {
        command.TaskId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>Queues an immediate run of the task; the outcome appears in its run history.</summary>
    [HttpPost("{id:guid}/run")]
    [RequirePermission(Permissions.ScheduledTasksManage)]
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
    [RequirePermission(Permissions.ScheduledTasksManage)]
    public async Task<IActionResult> CancelRun(Guid id, Guid runId, CancellationToken cancellationToken)
    {
        var canceled = await _mediator.Send(new CancelScheduledTaskRunCommand(id, runId), cancellationToken);
        return canceled
            ? Accepted()
            : Conflict(new { message = "That run is not currently in progress." });
    }
}
