using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Application.Features.QueryExecution.Commands;
using DotNetDBTasks.Application.Features.QueryExecution.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// User endpoints for viewing and executing assigned queries.
/// </summary>
[ApiController]
[Route("api/user/queries")]
[Authorize]
public class UserQueriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public UserQueriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Retrieves all queries available to the current user based on role assignments.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMyQueries(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetQueriesForUserQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Executes a dynamic query with provided parameters.
    /// </summary>
    [HttpPost("{id:guid}/execute")]
    public async Task<IActionResult> Execute(
        Guid id,
        [FromBody] Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var command = new ExecuteQueryCommand
        {
            QueryId = id,
            Parameters = parameters
        };
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns the selectable options for a dropdown parameter.
    /// Options are either the static list defined by the admin or the result of a lookup query.
    /// </summary>
    [HttpGet("{queryId:guid}/parameters/{parameterId:guid}/dropdown-options")]
    public async Task<IActionResult> GetDropdownOptions(
        Guid queryId,
        Guid parameterId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetParameterDropdownOptionsQuery { QueryId = queryId, ParameterId = parameterId },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the current user's query execution history.
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetMyHistory(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyExecutionHistoryQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves parameter change history for queries available to the user.
    /// </summary>
    [HttpGet("parameter-history")]
    public async Task<IActionResult> GetParameterChangeHistory(
        [FromQuery] Guid? queryId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetParameterChangeHistoryQuery { QueryId = queryId },
            cancellationToken);
        return Ok(result);
    }
}
