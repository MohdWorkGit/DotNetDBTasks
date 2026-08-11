using DotNetDBTasks.Application.Features.DatabaseUsers.Commands;
using DotNetDBTasks.Application.Features.DatabaseUsers.Queries;
using MediatR;
using DotNetDBTasks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Admin endpoints for managing database user credentials and access permissions.
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = RoleNames.Admin)]
public class DatabaseUsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public DatabaseUsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllDatabaseUsersQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDatabaseUserByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateDatabaseUserCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateDatabaseUserCommand command,
        CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteDatabaseUserCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Sets which application users can use this database user.
    /// </summary>
    [HttpPost("{id:guid}/access")]
    public async Task<IActionResult> AssignAccess(
        Guid id,
        [FromBody] AssignDatabaseUserAccessCommand command,
        CancellationToken cancellationToken)
    {
        command.DatabaseUserId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Tests the database connection using the stored encrypted credentials.
    /// </summary>
    [HttpPost("{id:guid}/test-connection")]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new TestDatabaseConnectionCommand { DatabaseUserId = id },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns the database users accessible to the current user (for dropdown selection).
    /// Available to all authenticated users.
    /// </summary>
    [HttpGet("accessible")]
    [Authorize]
    public async Task<IActionResult> GetAccessible(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAccessibleDatabaseUsersQuery(), cancellationToken);
        return Ok(result);
    }
}
