using DotNetDBTasks.API.Authorization;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Features.SystemAudit.Queries;
using DotNetDBTasks.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// The administrative audit trail: who created a user, granted a permission, edited a query.
///
/// <para>Read-only by design — the API exposes no way to edit or delete an entry, because an
/// audit trail an administrator can rewrite is not one.</para>
///
/// <para>Admin and Auditor. This is the Auditor's page: it is the oversight record their role
/// exists to review, and it complements the execution logs they already have.</para>
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize]
public class SystemAuditLogsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SystemAuditLogsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// One page of audit entries, newest first. All filters are optional and combine.
    /// </summary>
    [HttpGet]
    [RequirePermission(Permissions.AuditView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? category,
        [FromQuery] string? action,
        [FromQuery] Guid? userId,
        [FromQuery] bool? isSuccess,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetSystemAuditLogsQuery
        {
            Category = category,
            Action = action,
            UserId = userId,
            IsSuccess = isSuccess,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Search = search,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// The action codes and categories the system can emit, so the client can populate its
    /// filter dropdowns without hard-coding a list that drifts from the server's.
    /// </summary>
    [HttpGet("actions")]
    [RequirePermission(Permissions.AuditView)]
    public IActionResult GetActions() => Ok(new
    {
        categories = new[]
        {
            AuditActions.CategoryUsers, AuditActions.CategoryUserGroups, AuditActions.CategoryRoles,
            AuditActions.CategoryQueries, AuditActions.CategoryGroups,
            AuditActions.CategoryAccess, AuditActions.CategoryDatabaseUsers,
            AuditActions.CategoryScheduledTasks, AuditActions.CategoryDirectory,
            AuditActions.CategoryBranding, AuditActions.CategorySettings,
            AuditActions.CategoryOther
        },
        actions = AuditActions.AllActions().OrderBy(a => a).ToList()
    });
}
