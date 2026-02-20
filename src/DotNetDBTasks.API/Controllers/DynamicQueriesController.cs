using DotNetDBTasks.Application.Features.DynamicQueries.Commands;
using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Application.Features.QueryExecution.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Admin endpoints for managing dynamic queries.
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "Admin")]
public class DynamicQueriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public DynamicQueriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Retrieves all dynamic queries.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllDynamicQueriesQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves a specific dynamic query by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDynamicQueryByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Creates a new dynamic query with parameters.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateDynamicQueryCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Updates an existing dynamic query.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateDynamicQueryCommand command,
        CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Deletes a dynamic query.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteDynamicQueryCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Assigns a query to one or more roles.
    /// </summary>
    [HttpPost("{id:guid}/roles")]
    public async Task<IActionResult> AssignToRoles(
        Guid id,
        [FromBody] AssignQueryToRolesCommand command,
        CancellationToken cancellationToken)
    {
        command.QueryId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Retrieves execution logs with optional filters.
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? queryId,
        [FromQuery] Guid? userId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetExecutionLogsQuery { QueryId = queryId, UserId = userId },
            cancellationToken);
        return Ok(result);
    }
}
