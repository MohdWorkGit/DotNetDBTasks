using Bayan.API.Authorization;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;
using Bayan.Application.Features.Dashboards.Dtos;
using Bayan.Application.Features.Dashboards.Queries;
using Bayan.Application.Features.QueryExecution.Commands;
using Bayan.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Bayan.API.Controllers;

/// <summary>
/// Viewing side of dashboards: list what you may open, lay one out, and fetch each tile's data.
///
/// <para>Tiles are fetched one request each rather than as a whole dashboard, so a slow tile
/// delays only itself and every tile refreshes on its own interval. Each request is checked
/// against the dashboard grant and the tile's query grant before the shared cache is read.</para>
/// </summary>
[ApiController]
[Route("api/user/dashboards")]
[RequirePermission(Permissions.DashboardsRun)]
public class UserDashboardsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IQueryJobStore _jobStore;
    private readonly ICurrentUserService _currentUser;

    public UserDashboardsController(
        IMediator mediator,
        IQueryJobStore jobStore,
        ICurrentUserService currentUser)
    {
        _mediator = mediator;
        _jobStore = jobStore;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetMyDashboardsQuery(), cancellationToken));

    /// <summary>The layout, filters and tile settings. No data — each tile fetches its own.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetMyDashboardByIdQuery { Id = id }, cancellationToken));

    /// <summary>
    /// One tile's current data for the given filter values. A POST only so the filters travel as
    /// a body; it changes nothing. A tile that cannot be produced answers 200 with its error set,
    /// so one broken tile never looks like a broken dashboard.
    /// </summary>
    [HttpPost("{id:guid}/tiles/{tileId:guid}/data")]
    public async Task<IActionResult> GetTileData(
        Guid id,
        Guid tileId,
        [FromBody] DashboardTileDataRequest? request,
        CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetDashboardTileDataQuery
        {
            DashboardId = id,
            TileId = tileId,
            Filters = request?.Filters
        }, cancellationToken));

    /// <summary>
    /// Runs a tile's query in full — for the rows dialog or a download — and files the result into the job
    /// store, returning the job id the grid pages through
    /// <c>GET /api/user/queries/jobs/{jobId}/rows</c> — the same path the report viewer uses.
    /// </summary>
    [HttpPost("{id:guid}/tiles/{tileId:guid}/rows")]
    public async Task<IActionResult> RunTileRows(
        Guid id,
        Guid tileId,
        [FromBody] DashboardTileDataRequest? request,
        CancellationToken cancellationToken)
    {
        var (resolved, result) = await _mediator.Send(new RunDashboardTileRowsQuery
        {
            DashboardId = id,
            TileId = tileId,
            Filters = request?.Filters
        }, cancellationToken);

        var snapshot = new UserContextSnapshot(
            _currentUser.UserId, _currentUser.Username, _currentUser.Roles);

        var job = _jobStore.Create(_currentUser.UserId, snapshot, new ExecuteQueryCommand
        {
            QueryId = resolved.Query.Id,
            Parameters = resolved.Parameters,
            CacheFullResult = true
        });
        _jobStore.SetResult(job.Id, result);

        return Ok(new
        {
            jobId = job.Id,
            title = resolved.Tile.Title,
            columns = result.Columns,
            totalRows = result.TotalRows
        });
    }

    [HttpGet("{id:guid}/filters/{filterId:guid}/options")]
    public async Task<IActionResult> GetFilterOptions(
        Guid id,
        Guid filterId,
        CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetDashboardFilterOptionsQuery
        {
            DashboardId = id,
            FilterId = filterId
        }, cancellationToken));
}
