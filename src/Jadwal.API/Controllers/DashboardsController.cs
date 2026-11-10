using Jadwal.API.Authorization;
using Jadwal.API.Extensions;
using Jadwal.Application.Features.Transfer;
using Jadwal.Application.Features.Dashboards.Commands;
using Jadwal.Application.Features.Dashboards.Dtos;
using Jadwal.Application.Features.Dashboards.Queries;
using Jadwal.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jadwal.API.Controllers;

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

    /// <summary>
    /// Exports one dashboard as a portable JSON file. With <paramref name="includeDependencies"/>
    /// the file also carries what it needs elsewhere (see <see cref="ExportTransferQuery"/>).
    /// </summary>
    [HttpGet("{id:guid}/export")]
    [RequirePermission(Permissions.DashboardsTransfer)]
    public async Task<IActionResult> Export(
        Guid id, [FromQuery] bool includeDependencies = true, CancellationToken cancellationToken = default)
    {
        var file = await _mediator.Send(
            new ExportTransferQuery(TransferKind.Dashboard, id, includeDependencies), cancellationToken);
        var name = file.Dashboards.FirstOrDefault()?.Name ?? "dashboard";
        return TransferFiles.ToFile(file, $"{TransferFiles.Slug(name, "dashboard")}-{DateTime.Now:yyyyMMdd}.json");
    }

    /// <summary>Exports every dashboard as one JSON file. Import it at <c>api/admin/transfer/import</c>.</summary>
    [HttpGet("export")]
    [RequirePermission(Permissions.DashboardsTransfer)]
    public async Task<IActionResult> ExportAll(
        [FromQuery] bool includeDependencies = true, CancellationToken cancellationToken = default)
    {
        var file = await _mediator.Send(
            new ExportTransferQuery(TransferKind.Dashboard, null, includeDependencies), cancellationToken);
        return TransferFiles.ToFile(file, $"dashboards-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
    }
}
