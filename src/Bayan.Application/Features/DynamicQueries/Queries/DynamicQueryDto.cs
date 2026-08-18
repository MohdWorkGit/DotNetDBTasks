using Bayan.Domain.Enums;

namespace Bayan.Application.Features.DynamicQueries.Queries;

public class DynamicQueryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SqlQuery { get; set; } = string.Empty;
    /// <summary>Derived from SqlQuery server-side; read-only for clients.</summary>
    public QueryType QueryType { get; set; }
    public bool IsEnabled { get; set; }
    public int TimeoutSeconds { get; set; }
    public bool IsLongRunning { get; set; }
    public bool AllowRunWithoutConfirmation { get; set; }
    public bool SaveOldValues { get; set; }
    public Guid? DatabaseUserId { get; set; }
    public string? DatabaseUserName { get; set; }
    public Guid? QueryGroupId { get; set; }
    public string? QueryGroupName { get; set; }
    /// <summary>File name of the uploaded Word export template; null when none (default layout is used).</summary>
    public string? WordTemplateFileName { get; set; }
    /// <summary>
    /// Formats this query's results may be downloaded as, as <c>ExportFileFormat</c> names.
    /// Empty means export is off for this query. This is what the <em>query</em> permits; a
    /// caller additionally needs the matching <c>queries.export*</c> permission, which the
    /// client already knows and the API re-checks.
    /// </summary>
    public List<string> AllowedExportFormats { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public List<QueryParameterDto> Parameters { get; set; } = new();
    public List<RoleAssignmentDto> AssignedRoles { get; set; } = new();
    public List<UserGroupAssignmentDto> AssignedUserGroups { get; set; } = new();
    public List<UserAssignmentDto> AssignedUsers { get; set; } = new();
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

    // Dropdown-specific fields
    public bool AllowMultiple { get; set; }
    public DropdownSourceType? DropdownSourceType { get; set; }
    public string? DropdownStaticValues { get; set; }
    public Guid? DropdownQueryId { get; set; }
    public string? DropdownQueryValueColumn { get; set; }
    public string? DropdownQueryLabelColumn { get; set; }
}

public class DropdownOptionDto
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class RoleAssignmentDto
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
}

public class UserGroupAssignmentDto
{
    public Guid UserGroupId { get; set; }
    public string UserGroupName { get; set; } = string.Empty;
}

public class UserAssignmentDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
}
