using Bayan.API.Authorization;
using Bayan.Application.Features.Dashboards.Commands;
using Bayan.Application.Features.Dashboards.Dtos;
using Bayan.Application.Features.Dashboards.Queries;
using Bayan.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bayan.API.Controllers;

/// <summary>
/// Authoring side of dashboards: the definition and who may open it. Viewing one lives on
/// <see cref="UserDashboardsController"/>.
/// </summary>
[ApiController]
[Route("api/admin/dashboards")]
[Authorize]
public class DashboardsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DashboardsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission(Permissions.DashboardsView)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetDashboardsQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.DashboardsView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetDashboardByIdQuery { Id = id }, cancellationToken));

    [HttpPost]
    [RequirePermission(Permissions.DashboardsManage)]
    public async Task<IActionResult> Create(
        [FromBody] DashboardInput input,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SaveDashboardCommand { Input = input }, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.DashboardsManage)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] DashboardInput input,
        CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new SaveDashboardCommand { Id = id, Input = input }, cancellationToken));

    /// <summary>
    /// Runs a query once and returns its column names, for the builder's column pickers. It runs
    /// the query — there is no other way to learn its columns — with the caller's own access.
    /// </summary>
    [HttpPost("probe-columns")]
    [RequirePermission(Permissions.DashboardsManage)]
    public async Task<IActionResult> ProbeColumns(
        [FromBody] DashboardColumnsProbeRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new ProbeDashboardColumnsQuery
        {
            QueryId = request.QueryId,
            Parameters = request.Parameters
        }, cancellationToken));

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.DashboardsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteDashboardCommand { Id = id }, cancellationToken);
        return NoContent();
    }

    // ---------------------------------------------------------------- access

    [HttpGet("{id:guid}/access")]
    [RequirePermission(Permissions.AccessManageDashboard)]
    public async Task<IActionResult> GetAccess(Guid id, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetDashboardAccessQuery { Id = id }, cancellationToken));

    [HttpPut("{id:guid}/access")]
    [RequirePermission(Permissions.AccessManageDashboard)]
    public async Task<IActionResult> SetAccess(
        Guid id,
        [FromBody] DashboardAccessDto access,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetDashboardAccessCommand
        {
            DashboardId = id,
            RoleIds = access.RoleIds,
            UserGroupIds = access.UserGroupIds,
            UserIds = access.UserIds
        }, cancellationToken);

        return Ok(result);
    }
}
