using DotNetDBTasks.Application.Features.DynamicQueries.Commands;
using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Application.Features.QueryExecution.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Admin endpoints for managing dynamic queries.
/// Auditors have read access and can manage query accessibility (roles/departments/users assignments and logs).
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "Admin,Auditor")]
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
    /// Creates a new dynamic query with parameters. Requires Admin role.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(
        [FromBody] CreateDynamicQueryCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Updates an existing dynamic query. Requires Admin role.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
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
    /// Deletes a dynamic query. Requires Admin role.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
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
    /// Assigns a query to one or more departments.
    /// All users in those departments will gain access.
    /// </summary>
    [HttpPost("{id:guid}/departments")]
    public async Task<IActionResult> AssignToDepartments(
        Guid id,
        [FromBody] AssignQueryToDepartmentsCommand command,
        CancellationToken cancellationToken)
    {
        command.QueryId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Assigns a query to specific individual users.
    /// </summary>
    [HttpPost("{id:guid}/users")]
    public async Task<IActionResult> AssignToUsers(
        Guid id,
        [FromBody] AssignQueryToUsersCommand command,
        CancellationToken cancellationToken)
    {
        command.QueryId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Retrieves one page of execution logs with optional filters.
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? queryId,
        [FromQuery] Guid? userId,
        [FromQuery] bool? isSuccess,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] bool sortDescending = true,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetExecutionLogsQuery
            {
                QueryId = queryId,
                UserId = userId,
                IsSuccess = isSuccess,
                Search = search,
                SortBy = sortBy,
                SortDescending = sortDescending,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves one page of the pre-change row snapshots recorded for an execution log.
    /// </summary>
    [HttpGet("logs/{id:guid}/old-values")]
    public async Task<IActionResult> GetLogOldValues(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetExecutionLogOldValuesQuery
            {
                LogId = id,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            cancellationToken);
        return Ok(result);
    }
}
