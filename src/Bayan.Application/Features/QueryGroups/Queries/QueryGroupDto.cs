using Bayan.Application.Features.DynamicQueries.Queries;
using Bayan.Application.Features.Reports.Dtos;

namespace Bayan.Application.Features.QueryGroups.Queries;

public class QueryGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int QueryCount { get; set; }
    public List<RoleAssignmentDto> AssignedRoles { get; set; } = new();
    public List<UserGroupAssignmentDto> AssignedUserGroups { get; set; } = new();
    public List<UserAssignmentDto> AssignedUsers { get; set; } = new();
}

/// <summary>
/// A group as shown on the "My Queries" page — includes only the queries inside the group
/// that the current user can access (whether via direct/role/user-group access on the query
/// itself, or via group-level access).
/// </summary>
public class MyQueryGroupDto
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<DynamicQueryDto> Queries { get; set; } = new();

    /// <summary>
    /// The reports filed in this same group. They sit beside the queries rather than on a page
    /// of their own: to the person running one, a report is just another thing on their list,
    /// and splitting them would mean granting the same team the same folder twice.
    /// </summary>
    public List<ReportSummaryDto> Reports { get; set; } = new();
}
