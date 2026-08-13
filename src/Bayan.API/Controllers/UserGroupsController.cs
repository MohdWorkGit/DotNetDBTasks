using Bayan.API.Authorization;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Features.UserGroups.Commands;
using Bayan.Application.Features.UserGroups.Queries;
using MediatR;
using Bayan.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bayan.API.Controllers;

/// <summary>
/// Admin endpoints for the application's own user groups — the unit query and query-group
/// access is granted to. Nothing here talks to Active Directory: a group's membership is
/// whatever was set here, which is what lets access outlive a directory reorganisation and
/// cover locally created accounts.
///
/// <para>Reading needs <c>userGroups.view</c> and every write needs <c>userGroups.manage</c>,
/// both editable per role on Settings → Permissions. Whatever the matrix says, no non-Admin may
/// add <em>themselves</em> to a group — that would let them grant a query to a group and then
/// walk into it, which is the self-grant <c>AdminAccountGuard</c> exists to stop.</para>
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize]
public class UserGroupsController : ControllerBase
{
    private readonly IMediator _mediator;

    public UserGroupsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequirePermission(Permissions.UserGroupsView)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllUserGroupsQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.UserGroupsView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetUserGroupByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [RequirePermission(Permissions.UserGroupsManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserGroupCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.UserGroupsManage)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateUserGroupCommand command,
        CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.UserGroupsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteUserGroupCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Replaces the group's membership with exactly the users given.
    /// </summary>
    [HttpPut("{id:guid}/members")]
    [RequirePermission(Permissions.UserGroupsManage)]
    public async Task<IActionResult> SetMembers(
        Guid id,
        [FromBody] SetUserGroupMembersCommand command,
        CancellationToken cancellationToken)
    {
        command.GroupId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
