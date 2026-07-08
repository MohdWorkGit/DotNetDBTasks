using DotNetDBTasks.Domain.Entities;

namespace DotNetDBTasks.Domain.Interfaces;

/// <summary>
/// Unit of Work pattern interface ensuring transactional consistency across repositories.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    IRepository<User> Users { get; }
    IRepository<Role> Roles { get; }
    IRepository<DynamicQuery> DynamicQueries { get; }
    IRepository<QueryParameter> QueryParameters { get; }
    IRepository<QueryExecutionLog> QueryExecutionLogs { get; }
    IRepository<UserRole> UserRoles { get; }
    IRepository<DynamicQueryRole> DynamicQueryRoles { get; }
    IRepository<DynamicQueryDepartment> DynamicQueryDepartments { get; }
    IRepository<DynamicQueryUser> DynamicQueryUsers { get; }
    IRepository<QueryGroup> QueryGroups { get; }
    IRepository<QueryGroupRole> QueryGroupRoles { get; }
    IRepository<QueryGroupDepartment> QueryGroupDepartments { get; }
    IRepository<QueryGroupUser> QueryGroupUsers { get; }
    IRepository<DatabaseUser> DatabaseUsers { get; }
    IRepository<DatabaseUserRoleAccess> DatabaseUserRoleAccess { get; }
    IRepository<ScheduledTask> ScheduledTasks { get; }
    IRepository<ScheduledTaskTrigger> ScheduledTaskTriggers { get; }
    IRepository<ScheduledTaskItem> ScheduledTaskItems { get; }
    IRepository<ScheduledTaskRun> ScheduledTaskRuns { get; }
    IRepository<ScheduledTaskViewer> ScheduledTaskViewers { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
