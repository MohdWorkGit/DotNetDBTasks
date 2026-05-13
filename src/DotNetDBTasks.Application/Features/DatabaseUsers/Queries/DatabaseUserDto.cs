using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Application.Features.DatabaseUsers.Queries;

public class DatabaseUserDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DatabaseServerType ServerType { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string DbUsername { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<DatabaseUserRoleAccessDto> AssignedRoles { get; set; } = new();
}

public class DatabaseUserRoleAccessDto
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
}

/// <summary>
/// Lightweight DTO for dropdowns (e.g. when selecting a DB user for a query).
/// </summary>
public class DatabaseUserSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
