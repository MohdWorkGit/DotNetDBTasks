using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Admin endpoints for managing Active Directory / LDAP user access.
/// Allows searching AD users, importing by user or department, and revoking access.
/// </summary>
[ApiController]
[Route("api/admin/ldap")]
[Authorize(Roles = "Admin,Auditor")]
public class LdapUsersController : ControllerBase
{
    private readonly ILdapService _ldapService;
    private readonly IUnitOfWork _unitOfWork;

    public LdapUsersController(ILdapService ldapService, IUnitOfWork unitOfWork)
    {
        _ldapService = ldapService;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Searches LDAP directory for users matching the given term.
    /// </summary>
    [HttpGet("search")]
    [Authorize(Roles = "Admin")]
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
    public async Task<IActionResult> GetDepartments()
    {
        var departments = await _ldapService.GetDepartmentsAsync();
        return Ok(departments);
    }

    /// <summary>
    /// Returns all LDAP users in a given department, with import status.
    /// </summary>
    [HttpGet("departments/{department}/users")]
    [Authorize(Roles = "Admin")]
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
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ImportUsers([FromBody] ImportUsersRequest request)
    {
        if (request.Usernames == null || request.Usernames.Count == 0)
            return BadRequest("At least one username is required.");

        var userRole = (await _unitOfWork.Roles.FindAsync(r => r.Name == "User")).FirstOrDefault();
        if (userRole is null)
            return StatusCode(500, "User role not found in the system.");

        var imported = 0;
        foreach (var username in request.Usernames)
        {
            // Skip if already imported
            if (await _unitOfWork.Users.ExistsAsync(
                    u => u.Username == username && u.AuthSource == AuthSource.Ldap))
                continue;

            // Look up in LDAP
            var ldapUsers = await _ldapService.SearchUsersAsync(username);
            var ldapUser = ldapUsers.FirstOrDefault(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (ldapUser is null)
                continue;

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = ldapUser.Username,
                Email = ldapUser.Email,
                FirstName = ldapUser.FirstName,
                LastName = ldapUser.LastName,
                PasswordHash = "LDAP_AUTH",
                IsActive = true,
                AuthSource = AuthSource.Ldap,
                Department = ldapUser.Department,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.UserRoles.AddAsync(new UserRole
            {
                UserId = user.Id,
                RoleId = userRole.Id
            });

            imported++;
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok(new { Imported = imported });
    }

    /// <summary>
    /// Imports all LDAP users from a specific department.
    /// </summary>
    [HttpPost("import/department")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ImportDepartment([FromBody] ImportDepartmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Department))
            return BadRequest("Department is required.");

        var ldapUsers = await _ldapService.GetUsersByDepartmentAsync(request.Department);
        if (ldapUsers.Count == 0)
            return Ok(new { Imported = 0 });

        var userRole = (await _unitOfWork.Roles.FindAsync(r => r.Name == "User")).FirstOrDefault();
        if (userRole is null)
            return StatusCode(500, "User role not found in the system.");

        var existingUsernames = (await _unitOfWork.Users.FindAsync(
                u => u.AuthSource == AuthSource.Ldap))
            .Select(u => u.Username)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var imported = 0;
        foreach (var ldapUser in ldapUsers)
        {
            if (existingUsernames.Contains(ldapUser.Username))
                continue;

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = ldapUser.Username,
                Email = ldapUser.Email,
                FirstName = ldapUser.FirstName,
                LastName = ldapUser.LastName,
                PasswordHash = "LDAP_AUTH",
                IsActive = true,
                AuthSource = AuthSource.Ldap,
                Department = ldapUser.Department,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.UserRoles.AddAsync(new UserRole
            {
                UserId = user.Id,
                RoleId = userRole.Id
            });

            imported++;
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok(new { Imported = imported });
    }

    /// <summary>
    /// Revokes access for an LDAP user by deactivating their local account.
    /// </summary>
    [HttpPost("revoke/{username}")]
    [Authorize(Roles = "Admin")]
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

        return Ok();
    }

    /// <summary>
    /// Lists all imported LDAP users with their status.
    /// </summary>
    [HttpGet("imported")]
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
