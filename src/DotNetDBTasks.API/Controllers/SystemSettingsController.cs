using DotNetDBTasks.API.Authorization;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Constants;
using DotNetDBTasks.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Runtime settings an administrator can change without a restart.
///
/// <para>Reading needs no permission beyond being signed in — the client reads
/// <c>directoryEnabled</c> to decide whether to offer a menu entry, and a role that could not
/// read it would be shown a page that only 503s. Writing needs <c>settings.manage</c>.</para>
///
/// <para>Who may do what is <em>not</em> here: that moved to Settings → Permissions, which
/// edits the role matrix directly.</para>
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize]
public class SystemSettingsController : ControllerBase
{
    private readonly ISystemSettingsService _settings;
    private readonly IAuditLogger _audit;
    private readonly IAppLocalizer _messages;

    public SystemSettingsController(
        ISystemSettingsService settings, IAuditLogger audit, IAppLocalizer messages)
    {
        _settings = settings;
        _audit = audit;
        _messages = messages;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        return Ok(new SystemSettingsDto
        {
            SessionAccessTokenMinutes = await _settings.GetIntAsync(
                SystemSettingKeys.SessionAccessTokenMinutes,
                SystemSettingKeys.SessionAccessTokenMinutesDefault,
                cancellationToken),
            SessionRefreshTokenDays = await _settings.GetIntAsync(
                SystemSettingKeys.SessionRefreshTokenDays,
                SystemSettingKeys.SessionRefreshTokenDaysDefault,
                cancellationToken),
            QueryMaxRows = await _settings.GetIntAsync(
                SystemSettingKeys.QueryMaxRows,
                SystemSettingKeys.QueryMaxRowsDefault,
                cancellationToken),
            DirectoryEnabled = await _settings.GetBoolAsync(
                SystemSettingKeys.DirectoryEnabled,
                SystemSettingKeys.DirectoryEnabledDefault,
                cancellationToken)
        });
    }

    /// <summary>
    /// Replaces the settings.
    /// </summary>
    [HttpPut]
    [RequirePermission(Permissions.SettingsManage)]
    public async Task<IActionResult> Update(
        [FromBody] SystemSettingsDto request, CancellationToken cancellationToken)
    {
        // This controller bypasses MediatR, so there is no validation behavior to lean on.
        // The numbers need bounds regardless of who is asking: a five-second access token or a
        // million-row grid is a self-inflicted outage, and the browser's own min/max is a hint,
        // not a check.
        var problem = Validate(request);
        if (problem is not null)
            return BadRequest(new { message = problem });

        await _settings.SetIntAsync(
            SystemSettingKeys.SessionAccessTokenMinutes,
            request.SessionAccessTokenMinutes,
            cancellationToken);
        await _settings.SetIntAsync(
            SystemSettingKeys.SessionRefreshTokenDays,
            request.SessionRefreshTokenDays,
            cancellationToken);
        await _settings.SetIntAsync(
            SystemSettingKeys.QueryMaxRows,
            request.QueryMaxRows,
            cancellationToken);
        await _settings.SetBoolAsync(
            SystemSettingKeys.DirectoryEnabled,
            request.DirectoryEnabled,
            cancellationToken);

        // Recorded explicitly: this controller does not go through MediatR, so the audit
        // pipeline behavior never sees it — and a permission-widening toggle is precisely
        // what an auditor needs in the trail. The whole set goes in rather than one key,
        // because the client saves the object whole and any of them may have moved.
        await _audit.RecordAsync(new AuditEntry
        {
            Action = AuditActions.SettingsUpdated,
            Category = AuditActions.CategorySettings,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionAccessTokenMinutes = request.SessionAccessTokenMinutes,
                sessionRefreshTokenDays = request.SessionRefreshTokenDays,
                queryMaxRows = request.QueryMaxRows,
                directoryEnabled = request.DirectoryEnabled
            })
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>The first thing wrong with the request, or null when it is acceptable.</summary>
    private string? Validate(SystemSettingsDto request)
    {
        if (OutOfRange(request.SessionAccessTokenMinutes,
                SystemSettingKeys.AccessTokenMinutesMin, SystemSettingKeys.AccessTokenMinutesMax))
            return _messages[MessageKeys.SettingOutOfRange,
                SystemSettingKeys.SessionAccessTokenMinutes,
                SystemSettingKeys.AccessTokenMinutesMin,
                SystemSettingKeys.AccessTokenMinutesMax];

        if (OutOfRange(request.SessionRefreshTokenDays,
                SystemSettingKeys.RefreshTokenDaysMin, SystemSettingKeys.RefreshTokenDaysMax))
            return _messages[MessageKeys.SettingOutOfRange,
                SystemSettingKeys.SessionRefreshTokenDays,
                SystemSettingKeys.RefreshTokenDaysMin,
                SystemSettingKeys.RefreshTokenDaysMax];

        if (OutOfRange(request.QueryMaxRows,
                SystemSettingKeys.QueryMaxRowsMin, SystemSettingKeys.QueryMaxRowsMax))
            return _messages[MessageKeys.SettingOutOfRange,
                SystemSettingKeys.QueryMaxRows,
                SystemSettingKeys.QueryMaxRowsMin,
                SystemSettingKeys.QueryMaxRowsMax];

        // A refresh token that expires before the access token it renews could never be used.
        // The ranges above already rule this out — the shortest refresh (1 day) equals the
        // longest access token (1440 minutes) — so this cannot fire today. It is kept because
        // that coincidence is not a rule: widening AccessTokenMinutesMax or lowering
        // RefreshTokenDaysMin would make it reachable, and the two bounds are edited far away
        // from each other.
        if (TimeSpan.FromDays(request.SessionRefreshTokenDays)
            < TimeSpan.FromMinutes(request.SessionAccessTokenMinutes))
            return _messages[MessageKeys.RefreshShorterThanAccess];

        return null;
    }

    private static bool OutOfRange(int value, int min, int max) => value < min || value > max;
}

public class SystemSettingsDto
{
    /// <summary>How long an access token stays valid, in minutes.</summary>
    public int SessionAccessTokenMinutes { get; set; }

    /// <summary>How long a refresh token stays valid, in days.</summary>
    public int SessionRefreshTokenDays { get; set; }

    /// <summary>Rows a query may return to the screen before the result is capped.</summary>
    public int QueryMaxRows { get; set; }

    /// <summary>When false, the Active Directory pages are switched off.</summary>
    public bool DirectoryEnabled { get; set; }
}
