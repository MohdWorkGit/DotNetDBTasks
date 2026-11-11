using Jadwal.API.Authorization;
using Jadwal.Application.Features.Ai;
using Jadwal.Application.Features.Ai.Commands;
using Jadwal.Application.Features.Ai.Queries;
using Jadwal.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jadwal.API.Controllers;

/// <summary>
/// The local AI model (docs/LOCAL-AI-OPTIONS.md). Every action that sends something to the
/// model needs <c>ai.use</c> <em>and</em> the permission of the page it serves — two attributes,
/// because one attribute naming several permissions means any of them.
/// </summary>
[ApiController]
[Route("api/admin/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly IMediator _mediator;

    public AiController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Whether AI is on and the model answers. Any signed-in user: it names the model and
    /// nothing else, and the client asks before deciding whether to show AI buttons.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<AiStatusDto>> Status(CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(new GetAiStatusQuery(), cancellationToken));

    [HttpPost("explain-sql")]
    [RequirePermission(Permissions.AiUse)]
    [RequirePermission(Permissions.QueriesManage)]
    public async Task<ActionResult<AiSqlExplanationDto>> ExplainSql(
        [FromBody] ExplainSqlCommand command, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(command, cancellationToken));

    [HttpPost("suggest-fix")]
    [RequirePermission(Permissions.AiUse)]
    [RequirePermission(Permissions.QueriesManage)]
    public async Task<ActionResult<AiSqlFixDto>> SuggestFix(
        [FromBody] SuggestSqlFixCommand command, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(command, cancellationToken));

    /// <summary>Proposes dashboard tiles from saved queries. Returns a draft; saves nothing.</summary>
    [HttpPost("draft-dashboard")]
    [RequirePermission(Permissions.AiUse)]
    [RequirePermission(Permissions.DashboardsManage)]
    public async Task<ActionResult<AiDashboardDraftDto>> DraftDashboard(
        [FromBody] DraftDashboardCommand command, CancellationToken cancellationToken) =>
        Ok(await _mediator.Send(command, cancellationToken));
}
