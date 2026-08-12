using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Constants;
using DotNetDBTasks.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Runtime toggles an administrator can change without a restart.
///
/// <para>Reading is open to Access Managers as well: the client uses these to decide whether
/// to offer the per-query access page at all, and a role that cannot read the flag would be
/// shown a button that always 403s. Writing is Admin only.</para>
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = RoleNames.AdminOrAccessManager)]
public class SystemSettingsController : ControllerBase
{
    private readonly ISystemSettingsService _settings;
    private readonly IAuditLogger _audit;

    public SystemSettingsController(ISystemSettingsService settings, IAuditLogger audit)
    {
        _settings = settings;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        return Ok(new SystemSettingsDto
        {
            AccessManagerCanManageQueryAccess = await _settings.GetBoolAsync(
                SystemSettingKeys.AccessManagerCanManageQueryAccess,
                SystemSettingKeys.AccessManagerCanManageQueryAccessDefault,
                cancellationToken)
        });
    }

    /// <summary>
    /// Replaces the settings. Admin only — this widens or narrows what another role may do,
    /// so it is exactly the kind of change an Access Manager must not make for themselves.
    /// </summary>
    [HttpPut]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Update(
        [FromBody] SystemSettingsDto request, CancellationToken cancellationToken)
    {
        await _settings.SetBoolAsync(
            SystemSettingKeys.AccessManagerCanManageQueryAccess,
            request.AccessManagerCanManageQueryAccess,
            cancellationToken);

        // Recorded explicitly: this controller does not go through MediatR, so the audit
        // pipeline behavior never sees it — and a permission-widening toggle is precisely
        // what an auditor needs in the trail.
        await _audit.RecordAsync(new AuditEntry
        {
            Action = AuditActions.SettingsUpdated,
            Category = AuditActions.CategorySettings,
            EntityName = SystemSettingKeys.AccessManagerCanManageQueryAccess,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                accessManagerCanManageQueryAccess = request.AccessManagerCanManageQueryAccess
            })
        }, cancellationToken);

        return NoContent();
    }
}

public class SystemSettingsDto
{
    /// <summary>
    /// When false (the default), an Access Manager may only change access on query groups.
    /// </summary>
    public bool AccessManagerCanManageQueryAccess { get; set; }
}
