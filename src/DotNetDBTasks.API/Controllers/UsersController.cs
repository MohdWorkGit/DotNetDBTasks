using DotNetDBTasks.Application.Features.Users.Commands;
using DotNetDBTasks.Application.Features.Users.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Admin endpoints for managing system users.
/// Auditors have full user management access.
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "Admin,Auditor")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Retrieves all users with their roles.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllUsersQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves a specific user by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetUserByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Creates a new local user.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Changes a user's username.
    /// </summary>
    [HttpPut("{id:guid}/username")]
    public async Task<IActionResult> ChangeUsername(
        Guid id,
        [FromBody] ChangeUsernameCommand command,
        CancellationToken cancellationToken)
    {
        command.UserId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Changes a user's password (admin sets new password).
    /// </summary>
    [HttpPut("{id:guid}/password")]
    public async Task<IActionResult> ChangePassword(
        Guid id,
        [FromBody] ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        command.UserId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Resets a user's password to a temporary generated password.
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ResetPasswordCommand { UserId = id }, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Updates a user's role assignments.
    /// </summary>
    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> ChangeRoles(
        Guid id,
        [FromBody] ChangeUserRoleCommand command,
        CancellationToken cancellationToken)
    {
        command.UserId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Activates or deactivates a user.
    /// </summary>
    [HttpPut("{id:guid}/active")]
    public async Task<IActionResult> ToggleActive(
        Guid id,
        [FromBody] ToggleUserActiveCommand command,
        CancellationToken cancellationToken)
    {
        command.UserId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
