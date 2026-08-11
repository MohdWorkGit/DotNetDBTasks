using DotNetDBTasks.Domain.Interfaces;
using DotNetDBTasks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetDBTasks.API.Controllers;

/// <summary>
/// Lists the available roles. Access Managers need it to populate the role picker
/// on the query and query-group accessibility pages.
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = RoleNames.AdminOrAccessManager)]
public class RolesController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;

    public RolesController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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
}
