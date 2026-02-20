using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

public class DynamicQueryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SqlQuery { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int TimeoutSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<QueryParameterDto> Parameters { get; set; } = new();
    public List<RoleAssignmentDto> AssignedRoles { get; set; } = new();
}

public class QueryParameterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public int SortOrder { get; set; }
}

public class RoleAssignmentDto
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
}
