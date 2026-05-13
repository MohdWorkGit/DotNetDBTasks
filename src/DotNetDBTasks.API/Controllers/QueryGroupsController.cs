using DotNetDBTasks.Application.Features.QueryGroups.Commands;
using DotNetDBTasks.Application.Features.QueryGroups.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Admin endpoints for managing query groups (folders).
/// Auditors have read access and can manage group accessibility.
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "Admin,Auditor")]
public class QueryGroupsController : ControllerBase
{
    private readonly IMediator _mediator;

    public QueryGroupsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllQueryGroupsQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetQueryGroupByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(
        [FromBody] CreateQueryGroupCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateQueryGroupCommand command,
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
        await _mediator.Send(new DeleteQueryGroupCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/roles")]
    public async Task<IActionResult> AssignToRoles(
        Guid id,
        [FromBody] AssignQueryGroupToRolesCommand command,
        CancellationToken cancellationToken)
    {
        command.GroupId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/departments")]
    public async Task<IActionResult> AssignToDepartments(
        Guid id,
        [FromBody] AssignQueryGroupToDepartmentsCommand command,
        CancellationToken cancellationToken)
    {
        command.GroupId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/users")]
    public async Task<IActionResult> AssignToUsers(
        Guid id,
        [FromBody] AssignQueryGroupToUsersCommand command,
        CancellationToken cancellationToken)
    {
        command.GroupId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
