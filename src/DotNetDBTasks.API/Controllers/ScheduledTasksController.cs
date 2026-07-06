using DotNetDBTasks.Application.Features.ScheduledTasks.Commands;
using DotNetDBTasks.Application.Features.ScheduledTasks.Queries;
using MediatR;
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

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(
        [FromBody] CreateScheduledTaskCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
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
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteScheduledTaskCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Queues an immediate run of the task; the outcome appears in its run history.</summary>
    [HttpPost("{id:guid}/run")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RunNow(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RunScheduledTaskNowCommand(id), cancellationToken);
        return Accepted();
    }
}
