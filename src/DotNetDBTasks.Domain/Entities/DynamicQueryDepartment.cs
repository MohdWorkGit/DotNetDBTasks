namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Join entity representing access to a DynamicQuery granted to all users in a department.
/// </summary>
public class DynamicQueryDepartment
{
    public Guid DynamicQueryId { get; set; }
    public DynamicQuery DynamicQuery { get; set; } = null!;

    public string Department { get; set; } = string.Empty;
}
