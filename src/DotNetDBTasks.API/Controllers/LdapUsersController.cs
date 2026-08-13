using DotNetDBTasks.API.Authorization;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Interfaces;
using DotNetDBTasks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Admin endpoints for managing Active Directory / LDAP user access.
/// Allows searching AD users, importing by user or department, and revoking access.
///
/// <para>Access Managers reach only the two unannotated read actions — the department
/// list and the imported-user list — which populate the pickers on the accessibility
/// pages. Every action that touches AD or changes access is Admin only, so a new
/// action here needs its own attribute.</para>
/// </summary>
[ApiController]
[Route("api/admin/ldap")]
[Authorize]
public class LdapUsersController : ControllerBase, IAsyncActionFilter
{
    private readonly ILdapService _ldapService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppLocalizer _messages;
    private readonly IAuditLogger _audit;
    private readonly ISystemSettingsService _settings;

    public LdapUsersController(
        ILdapService ldapService,
        IUnitOfWork unitOfWork,
        IAppLocalizer messages,
        IAuditLogger audit,
        ISystemSettingsService settings)
    {
        _ldapService = ldapService;
        _unitOfWork = unitOfWork;
        _messages = messages;
        _audit = audit;
        _settings = settings;
    }

    /// <summary>
    /// Turns the whole controller off when an administrator has said this installation has no
    /// directory.
    ///
    /// <para>Done here rather than per action so a newly added endpoint cannot forget it — the
    /// mirror of the warning above about <c>[Authorize]</c>. 503 rather than 404: the feature
    /// exists and is switched off, which is what an operator needs to be told.</para>
    ///
    /// <para>Nothing here is on a sign-in path, so this cannot lock anyone out. Accounts already
    /// imported from the directory keep authenticating against it either way.</para>
    ///
    /// <para>The controller implements <see cref="IAsyncActionFilter"/> for this — MVC runs a
    /// controller that is also a filter around its own actions. <c>ControllerBase</c> has no
    /// method to override; only the heavier <c>Controller</c> does. <c>[NonAction]</c> is not
    /// optional: every public method on a controller is a route by convention, and without it
    /// MVC tries to bind two request bodies to this one and refuses to start.</para>
    /// </summary>
    [NonAction]
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var enabled = await _settings.GetBoolAsync(
            SystemSettingKeys.DirectoryEnabled,
            SystemSettingKeys.DirectoryEnabledDefault,
            context.HttpContext.RequestAborted);

        if (!enabled)
        {
            context.Result = StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = _messages[MessageKeys.DirectoryDisabled] });
            return;
        }

        await next();
    }

    /// <summary>
    /// These endpoints act on the controller rather than through MediatR, so the audit
    /// pipeline behavior never sees them — they have to record themselves.
    /// </summary>
    private Task AuditAsync(string action, string? entityName, object? details, CancellationToken ct = default) =>
        _audit.RecordAsync(new AuditEntry
        {
            Action = action,
            Category = AuditActions.CategoryDirectory,
            EntityName = entityName,
            DetailsJson = details is null ? null : System.Text.Json.JsonSerializer.Serialize(details)
        }, ct);

    /// <summary>
    /// Searches LDAP directory for users matching the given term.
    /// </summary>
    [HttpGet("search")]
    [RequirePermission(Permissions.DirectoryManage)]
    public async Task<IActionResult> SearchUsers([FromQuery] string term)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length < 2)
            return BadRequest("Search term must be at least 2 characters.");

        var ldapUsers = await _ldapService.SearchUsersAsync(term);

        // Check which ones are already imported
        var usernames = ldapUsers.Select(u => u.Username).ToList();
        var existingUsers = await _unitOfWork.Users.FindAsync(
            u => usernames.Contains(u.Username) && u.AuthSource == AuthSource.Ldap);
        var importedUsernames = existingUsers.Select(u => u.Username).ToHashSet();

        var result = ldapUsers.Select(u => new
        {
            u.Username,
            u.Email,
            u.FirstName,
            u.LastName,
            u.Department,
            IsImported = importedUsernames.Contains(u.Username)
        });

        return Ok(result);
    }

    /// <summary>
    /// Returns all departments from the LDAP directory.
    /// </summary>
    [HttpGet("departments")]
    [RequirePermission(Permissions.DirectoryView)]
    public async Task<IActionResult> GetDepartments()
    {
        var departments = await _ldapService.GetDepartmentsAsync();
        return Ok(departments);
    }

    /// <summary>
    /// Returns all LDAP users in a given department, with import status.
    /// </summary>
    [HttpGet("departments/{department}/users")]
    [RequirePermission(Permissions.DirectoryManage)]
    public async Task<IActionResult> GetDepartmentUsers(string department)
    {
        var ldapUsers = await _ldapService.GetUsersByDepartmentAsync(department);

        var usernames = ldapUsers.Select(u => u.Username).ToList();
        var existingUsers = await _unitOfWork.Users.FindAsync(
            u => usernames.Contains(u.Username) && u.AuthSource == AuthSource.Ldap);
        var importedUsernames = existingUsers.Select(u => u.Username).ToHashSet();

        var result = ldapUsers.Select(u => new
        {
            u.Username,
            u.Email,
            u.FirstName,
            u.LastName,
            u.Department,
            IsImported = importedUsernames.Contains(u.Username)
        });

        return Ok(result);
    }

    /// <summary>
    /// Imports (grants access to) specific LDAP users by username.
    /// Creates local user records with AuthSource=Ldap and assigns the User role.
    /// </summary>
    [HttpPost("import/users")]
    [RequirePermission(Permissions.DirectoryManage)]
    public async Task<IActionResult> ImportUsers([FromBody] ImportUsersRequest request)
    {
        if (request.Usernames == null || request.Usernames.Count == 0)
            return BadRequest("At least one username is required.");

        var userRole = (await _unitOfWork.Roles.FindAsync(r => r.Name == RoleNames.User)).FirstOrDefault();
        if (userRole is null)
            return StatusCode(500, "User role not found in the system.");

        var result = new ImportResultDto();
        var claimedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var username in request.Usernames)
        {
            if (await _unitOfWork.Users.ExistsAsync(
                    u => u.Username == username && u.AuthSource == AuthSource.Ldap))
            {
                result.AlreadyImported.Add(username);
                continue;
            }

            var ldapUsers = await _ldapService.SearchUsersAsync(username);
            var ldapUser = ldapUsers.FirstOrDefault(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (ldapUser is null)
            {
                result.NotFound.Add(username);
                continue;
            }

            var skip = await DescribeBlockerAsync(ldapUser, claimedEmails);
            if (skip is not null)
            {
                result.Skipped.Add(new ImportSkipDto { Username = username, Reason = skip });
                continue;
            }

            await StageImportAsync(ldapUser, userRole.Id, claimedEmails);
            result.Imported++;
        }

        await _unitOfWork.SaveChangesAsync();
        result.Summary = Summarise(result);
        await AuditAsync(AuditActions.DirectoryImportUsers, null, new
        {
            requested = request.Usernames.Count,
            result.Imported,
            alreadyPresent = result.AlreadyImported.Count,
            notFound = result.NotFound.Count,
            skipped = result.Skipped.Count
        });
        return Ok(result);
    }

    /// <summary>
    /// Why this directory account cannot be imported, or null when it can. Checked up front
    /// because the whole batch is one SaveChanges: letting the database reject a single row
    /// would roll back every other user in the request and surface as one opaque error.
    /// </summary>
    private async Task<string?> DescribeBlockerAsync(LdapUserInfo ldapUser, HashSet<string> claimedEmails)
    {
        if (await _unitOfWork.Users.ExistsAsync(u => u.Username == ldapUser.Username))
            return _messages[MessageKeys.ImportLocalAccountExists, ldapUser.Username];

        if (string.IsNullOrWhiteSpace(ldapUser.Email))
            return null;

        if (claimedEmails.Contains(ldapUser.Email))
            return _messages[MessageKeys.ImportEmailClashInBatch, ldapUser.Email!];

        if (await _unitOfWork.Users.ExistsAsync(u => u.Email == ldapUser.Email))
            return _messages[MessageKeys.ImportEmailTaken, ldapUser.Email!];

        return null;
    }

    private async Task StageImportAsync(LdapUserInfo ldapUser, Guid roleId, HashSet<string> claimedEmails)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = ldapUser.Username,
            Email = string.IsNullOrWhiteSpace(ldapUser.Email) ? null : ldapUser.Email,
            FirstName = ldapUser.FirstName,
            LastName = ldapUser.LastName,
            PasswordHash = "LDAP_AUTH",
            IsActive = true,
            AuthSource = AuthSource.Ldap,
            Department = ldapUser.Department,
            CreatedAt = DateTime.UtcNow
        };

        if (user.Email is not null)
            claimedEmails.Add(user.Email);

        await _unitOfWork.Users.AddAsync(user);
        await _unitOfWork.UserRoles.AddAsync(new UserRole { UserId = user.Id, RoleId = roleId });
    }

    /// <summary>True when some other account already holds this address. Null is never taken.</summary>
    private async Task<bool> EmailTakenByOtherAsync(string? email, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        return await _unitOfWork.Users.ExistsAsync(u => u.Email == email && u.Id != userId);
    }

    /// <summary>Builds the one-line message the client shows after an import.</summary>
    private string Summarise(ImportResultDto result)
    {
        var parts = new List<string> { _messages[MessageKeys.ImportSummaryImported, result.Imported] };

        if (result.AlreadyImported.Count > 0)
            parts.Add(_messages[MessageKeys.ImportSummaryAlreadyPresent, result.AlreadyImported.Count]);
        if (result.NotFound.Count > 0)
            parts.Add(_messages[MessageKeys.ImportSummaryNotFound, result.NotFound.Count]);
        if (result.Skipped.Count > 0)
            parts.Add(_messages[MessageKeys.ImportSummarySkipped, result.Skipped.Count]);

        var summary = string.Join(", ", parts) + ".";

        // Reasons are the part worth reading; without them "3 skipped" is as unhelpful as the
        // generic error it replaced. Cap the list so a large batch stays readable.
        if (result.Skipped.Count > 0)
        {
            var reasons = result.Skipped.Take(5).Select(s => $"{s.Username}: {s.Reason}");
            summary += " " + string.Join(" ", reasons);
            if (result.Skipped.Count > 5)
                summary += " " + _messages[MessageKeys.ImportSummaryMore, result.Skipped.Count - 5];
        }

        return summary;
    }

    /// <summary>
    /// Imports all LDAP users from a specific department.
    /// </summary>
    [HttpPost("import/department")]
    [RequirePermission(Permissions.DirectoryManage)]
    public async Task<IActionResult> ImportDepartment([FromBody] ImportDepartmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Department))
            return BadRequest("Department is required.");

        var ldapUsers = await _ldapService.GetUsersByDepartmentAsync(request.Department);
        if (ldapUsers.Count == 0)
            return Ok(new ImportResultDto
            {
                Summary = _messages[MessageKeys.ImportNoDepartmentMatches, request.Department]
            });

        var userRole = (await _unitOfWork.Roles.FindAsync(r => r.Name == RoleNames.User)).FirstOrDefault();
        if (userRole is null)
            return StatusCode(500, "User role not found in the system.");

        var existingUsernames = (await _unitOfWork.Users.FindAsync(
                u => u.AuthSource == AuthSource.Ldap))
            .Select(u => u.Username)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new ImportResultDto();
        var claimedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ldapUser in ldapUsers)
        {
            if (existingUsernames.Contains(ldapUser.Username))
            {
                result.AlreadyImported.Add(ldapUser.Username);
                continue;
            }

            var skip = await DescribeBlockerAsync(ldapUser, claimedEmails);
            if (skip is not null)
            {
                result.Skipped.Add(new ImportSkipDto { Username = ldapUser.Username, Reason = skip });
                continue;
            }

            await StageImportAsync(ldapUser, userRole.Id, claimedEmails);
            result.Imported++;
        }

        await _unitOfWork.SaveChangesAsync();
        result.Summary = Summarise(result);
        await AuditAsync(AuditActions.DirectoryImportDepartment, request.Department, new
        {
            request.Department,
            result.Imported,
            alreadyPresent = result.AlreadyImported.Count,
            skipped = result.Skipped.Count
        });
        return Ok(result);
    }

    /// <summary>
    /// Revokes access for an LDAP user by deactivating their local account.
    /// </summary>
    [HttpPost("revoke/{username}")]
    [RequirePermission(Permissions.DirectoryManage)]
    public async Task<IActionResult> RevokeAccess(string username)
    {
        var users = await _unitOfWork.Users.FindAsync(
            u => u.Username == username && u.AuthSource == AuthSource.Ldap);
        var user = users.FirstOrDefault();

        if (user is null)
            return NotFound("LDAP user not found in the system.");

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync();

        await AuditAsync(AuditActions.DirectoryRevoke, username, new { username });
        return Ok();
    }

    /// <summary>
    /// Restores access for a previously revoked LDAP user.
    /// </summary>
    [HttpPost("restore/{username}")]
    [RequirePermission(Permissions.DirectoryManage)]
    public async Task<IActionResult> RestoreAccess(string username)
    {
        var users = await _unitOfWork.Users.FindAsync(
            u => u.Username == username && u.AuthSource == AuthSource.Ldap);
        var user = users.FirstOrDefault();

        if (user is null)
            return NotFound("LDAP user not found in the system.");

        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync();

        await AuditAsync(AuditActions.DirectoryRestore, username, new { username });
        return Ok();
    }

    /// <summary>
    /// Syncs department (and profile fields) for all imported LDAP users from the directory.
    /// </summary>
    [HttpPost("sync")]
    [RequirePermission(Permissions.DirectoryManage)]
    public async Task<IActionResult> SyncImportedUsers()
    {
        var importedUsers = await _unitOfWork.Users.FindAsync(u => u.AuthSource == AuthSource.Ldap);

        var synced = 0;
        var notFound = 0;

        foreach (var user in importedUsers)
        {
            var ldapUser = await _ldapService.GetUserByUsernameAsync(user.Username);
            if (ldapUser is null)
            {
                notFound++;
                continue;
            }

            var changed = false;
            if (user.Department != ldapUser.Department) { user.Department = ldapUser.Department; changed = true; }

            // Email is unique-indexed, and one collision here would roll back the whole sync
            // run — so adopt the directory's address only when no other account holds it.
            if (user.Email != ldapUser.Email
                && !await EmailTakenByOtherAsync(ldapUser.Email, user.Id))
            {
                user.Email = ldapUser.Email;
                changed = true;
            }

            if (user.FirstName != ldapUser.FirstName) { user.FirstName = ldapUser.FirstName; changed = true; }
            if (user.LastName != ldapUser.LastName) { user.LastName = ldapUser.LastName; changed = true; }

            if (changed)
            {
                user.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.Users.Update(user);
                synced++;
            }
        }

        await _unitOfWork.SaveChangesAsync();
        await AuditAsync(AuditActions.DirectorySync, null, new { synced, notFound });
        return Ok(new { Synced = synced, NotFound = notFound });
    }

    /// <summary>
    /// Lists all imported LDAP users with their status.
    /// </summary>
    [HttpGet("imported")]
    [RequirePermission(Permissions.DirectoryView)]
    public async Task<IActionResult> GetImportedUsers()
    {
        var users = await _unitOfWork.Users.FindAsync(u => u.AuthSource == AuthSource.Ldap);
        var result = users.Select(u => new
        {
            u.Id,
            u.Username,
            u.Email,
            u.FirstName,
            u.LastName,
            u.Department,
            u.IsActive,
            u.CreatedAt
        }).OrderBy(u => u.Department).ThenBy(u => u.Username);

        return Ok(result);
    }
}

public class ImportUsersRequest
{
    public List<string> Usernames { get; set; } = new();
}

public class ImportDepartmentRequest
{
    public string Department { get; set; } = string.Empty;
}

/// <summary>
/// Outcome of an import. Reports what did not get imported and why, rather than only a count:
/// an import that quietly returns 0 leaves the operator with nothing to act on.
/// </summary>
public class ImportResultDto
{
    public int Imported { get; set; }

    /// <summary>Requested usernames the directory had no match for.</summary>
    public List<string> NotFound { get; set; } = new();

    /// <summary>Already present locally; nothing to do.</summary>
    public List<string> AlreadyImported { get; set; } = new();

    /// <summary>Rejected, with the reason — a clashing email address, most often.</summary>
    public List<ImportSkipDto> Skipped { get; set; } = new();

    /// <summary>One line summarising the whole run, ready to show in a toast.</summary>
    public string Summary { get; set; } = string.Empty;
}

public class ImportSkipDto
{
    public string Username { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
