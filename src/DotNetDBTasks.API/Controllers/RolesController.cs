using DotNetDBTasks.API.Authorization;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Constants;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Roles and what each one may do — the data behind Settings → Permissions.
///
/// <para>Listing is open to any signed-in caller: role names are not secret, and the access
/// pickers on the query and query-group pages are built from them. Everything that changes a
/// role needs <c>roles.manage</c>.</para>
///
/// <para>Two rules hold whatever the matrix says. <b>Admin is pinned</b>: it holds every
/// permission, cannot be edited and cannot be deleted, so an installation cannot be locked out
/// of its own Permissions tab. <b>Seeded roles cannot be renamed or deleted</b> — code and
/// translations refer to them by name, and the seeder would recreate them anyway.</para>
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppLocalizer _messages;
    private readonly IAuditLogger _audit;

    public RolesController(IUnitOfWork unitOfWork, IAppLocalizer messages, IAuditLogger audit)
    {
        _unitOfWork = unitOfWork;
        _messages = messages;
        _audit = audit;
    }

    /// <summary>
    /// Retrieves all roles in the system.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var roles = await _unitOfWork.Roles.GetAllAsync(cancellationToken);
        return Ok(roles.Select(r => new { r.Id, r.Name, r.Description }));
    }

    /// <summary>
    /// The whole matrix: every permission the system defines, and every role with the ones it
    /// holds. One call, because the page is a grid — fetching it row by row would just make the
    /// client reassemble what the server already knows.
    /// </summary>
    [HttpGet("permissions")]
    [RequirePermission(Permissions.RolesManage)]
    public async Task<IActionResult> GetMatrix(CancellationToken cancellationToken)
    {
        var roles = await _unitOfWork.Roles.GetAllAsync(cancellationToken, "RolePermissions");

        return Ok(new
        {
            permissions = Permissions.All,
            roles = roles
                .OrderByDescending(r => Permissions.IsPinned(r.Name))
                .ThenByDescending(r => r.IsSeeded)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(r => new
                {
                    r.Id,
                    r.Name,
                    r.Description,
                    r.IsSeeded,
                    isPinned = Permissions.IsPinned(r.Name),
                    // A pinned role reports the full set rather than its (empty) rows, so the
                    // grid shows what is true instead of what is stored.
                    permissions = Permissions.IsPinned(r.Name)
                        ? Permissions.All
                        : r.RolePermissions.Select(p => p.Permission).ToList()
                })
        });
    }

    /// <summary>
    /// Replaces one role's permissions with exactly the list given.
    /// </summary>
    [HttpPut("{id:guid}/permissions")]
    [RequirePermission(Permissions.RolesManage)]
    public async Task<IActionResult> SetPermissions(
        Guid id, [FromBody] SetRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles.GetByIdAsync(id, cancellationToken, "RolePermissions");
        if (role is null)
            return NotFound();

        if (Permissions.IsPinned(role.Name))
            return BadRequest(new { message = _messages[MessageKeys.RoleIsPinned, role.Name] });

        var wanted = (request.Permissions ?? new List<string>())
            .Where(p => Permissions.All.Contains(p))
            .Distinct()
            .ToHashSet();

        var held = role.RolePermissions.Select(p => p.Permission).ToHashSet();

        foreach (var gone in role.RolePermissions.Where(p => !wanted.Contains(p.Permission)).ToList())
            _unitOfWork.RolePermissions.Delete(gone);

        foreach (var added in wanted.Where(p => !held.Contains(p)))
            await _unitOfWork.RolePermissions.AddAsync(
                new RolePermission { RoleId = role.Id, Permission = added }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Recorded by hand: this controller does not go through MediatR, and a change to what a
        // whole role may do is exactly what an auditor comes here to find. The granted and
        // revoked lists say what moved, which a snapshot of the new set would not.
        await AuditAsync(AuditActions.RolesSetPermissions, role.Name, new
        {
            role = role.Name,
            granted = wanted.Except(held).OrderBy(p => p).ToList(),
            revoked = held.Except(wanted).OrderBy(p => p).ToList()
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Creates a role with the permissions given. The name must be unused.
    /// </summary>
    [HttpPost]
    [RequirePermission(Permissions.RolesManage)]
    public async Task<IActionResult> Create(
        [FromBody] SaveRoleRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            return BadRequest(new { message = _messages[MessageKeys.RoleNameRequired] });

        if (await _unitOfWork.Roles.ExistsAsync(r => r.Name == name, cancellationToken))
            return BadRequest(new { message = _messages[MessageKeys.RoleNameTaken, name] });

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = request.Description,
            IsSeeded = false,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Roles.AddAsync(role, cancellationToken);

        foreach (var permission in (request.Permissions ?? new List<string>())
                     .Where(p => Permissions.All.Contains(p)).Distinct())
        {
            await _unitOfWork.RolePermissions.AddAsync(
                new RolePermission { RoleId = role.Id, Permission = permission }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await AuditAsync(AuditActions.RolesCreate, role.Name, new { role.Name, request.Permissions }, cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { id = role.Id }, new { role.Id, role.Name, role.Description });
    }

    /// <summary>
    /// Renames a custom role or edits its description. Seeded roles keep their names.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.RolesManage)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] SaveRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles.GetByIdAsync(id, cancellationToken);
        if (role is null)
            return NotFound();

        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            return BadRequest(new { message = _messages[MessageKeys.RoleNameRequired] });

        if (role.IsSeeded && !string.Equals(name, role.Name, StringComparison.Ordinal))
            return BadRequest(new { message = _messages[MessageKeys.RoleIsSeeded, role.Name] });

        if (await _unitOfWork.Roles.ExistsAsync(r => r.Name == name && r.Id != id, cancellationToken))
            return BadRequest(new { message = _messages[MessageKeys.RoleNameTaken, name] });

        var previousName = role.Name;
        role.Name = name;
        role.Description = request.Description;
        role.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Roles.Update(role);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await AuditAsync(AuditActions.RolesUpdate, role.Name, new { previousName, role.Name }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Deletes a custom role. Refused while anyone still holds it — reassigning those people is
    /// a decision for whoever is deleting the role, not a side effect of deleting it.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.RolesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles.GetByIdAsync(id, cancellationToken);
        if (role is null)
            return NotFound();

        if (role.IsSeeded)
            return BadRequest(new { message = _messages[MessageKeys.RoleIsSeeded, role.Name] });

        var holders = await _unitOfWork.UserRoles.CountAsync(ur => ur.RoleId == id, cancellationToken);
        if (holders > 0)
            return BadRequest(new { message = _messages[MessageKeys.RoleStillAssigned, role.Name, holders] });

        _unitOfWork.Roles.Delete(role);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await AuditAsync(AuditActions.RolesDelete, role.Name, new { role.Name }, cancellationToken);

        return NoContent();
    }

    private Task AuditAsync(string action, string? entityName, object details, CancellationToken ct) =>
        _audit.RecordAsync(new AuditEntry
        {
            Action = action,
            Category = AuditActions.CategoryRoles,
            EntityName = entityName,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(details)
        }, ct);
}

public class SetRolePermissionsRequest
{
    /// <summary>The permissions the role should hold. Unknown names are ignored.</summary>
    public List<string> Permissions { get; set; } = new();
}

public class SaveRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Starting permissions. Only read when creating; ignored on update.</summary>
    public List<string> Permissions { get; set; } = new();
}
