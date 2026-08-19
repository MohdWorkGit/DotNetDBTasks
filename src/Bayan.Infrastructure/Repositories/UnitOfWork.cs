using Bayan.Domain.Entities;
using Bayan.Domain.Interfaces;
using Bayan.Infrastructure.Data;

namespace Bayan.Infrastructure.Repositories;

/// <summary>
/// Unit of Work implementation coordinating changes across multiple repositories.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private bool _disposed;

    public IRepository<User> Users { get; }
    public IRepository<Role> Roles { get; }
    public IRepository<DynamicQuery> DynamicQueries { get; }
    public IRepository<QueryParameter> QueryParameters { get; }
    public IRepository<QueryExecutionLog> QueryExecutionLogs { get; }
    public IRepository<UserRole> UserRoles { get; }
    public IRepository<RolePermission> RolePermissions { get; }
    public IRepository<UserGroup> UserGroups { get; }
    public IRepository<UserGroupMember> UserGroupMembers { get; }
    public IRepository<DynamicQueryRole> DynamicQueryRoles { get; }
    public IRepository<DynamicQueryUserGroup> DynamicQueryUserGroups { get; }
    public IRepository<DynamicQueryUser> DynamicQueryUsers { get; }
    public IRepository<QueryGroup> QueryGroups { get; }
    public IRepository<QueryGroupRole> QueryGroupRoles { get; }
    public IRepository<QueryGroupUserGroup> QueryGroupUserGroups { get; }
    public IRepository<QueryGroupUser> QueryGroupUsers { get; }
    public IRepository<DatabaseUser> DatabaseUsers { get; }
    public IRepository<DatabaseUserRoleAccess> DatabaseUserRoleAccess { get; }
    public IRepository<ScheduledTask> ScheduledTasks { get; }
    public IRepository<ScheduledTaskTrigger> ScheduledTaskTriggers { get; }
    public IRepository<ScheduledTaskItem> ScheduledTaskItems { get; }
    public IRepository<ScheduledTaskRun> ScheduledTaskRuns { get; }
    public IRepository<ScheduledTaskViewer> ScheduledTaskViewers { get; }
    public IRepository<ScheduledTaskViewerRole> ScheduledTaskViewerRoles { get; }
    public IRepository<ScheduledTaskViewerUserGroup> ScheduledTaskViewerUserGroups { get; }
    public IRepository<SystemTemplate> SystemTemplates { get; }
    public IRepository<SystemAuditLog> SystemAuditLogs { get; }
    public IRepository<SystemSetting> SystemSettings { get; }

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
        Users = new Repository<User>(context);
        Roles = new Repository<Role>(context);
        DynamicQueries = new Repository<DynamicQuery>(context);
        QueryParameters = new Repository<QueryParameter>(context);
        QueryExecutionLogs = new Repository<QueryExecutionLog>(context);
        UserRoles = new Repository<UserRole>(context);
        RolePermissions = new Repository<RolePermission>(context);
        UserGroups = new Repository<UserGroup>(context);
        UserGroupMembers = new Repository<UserGroupMember>(context);
        DynamicQueryRoles = new Repository<DynamicQueryRole>(context);
        DynamicQueryUserGroups = new Repository<DynamicQueryUserGroup>(context);
        DynamicQueryUsers = new Repository<DynamicQueryUser>(context);
        QueryGroups = new Repository<QueryGroup>(context);
        QueryGroupRoles = new Repository<QueryGroupRole>(context);
        QueryGroupUserGroups = new Repository<QueryGroupUserGroup>(context);
        QueryGroupUsers = new Repository<QueryGroupUser>(context);
        DatabaseUsers = new Repository<DatabaseUser>(context);
        DatabaseUserRoleAccess = new Repository<DatabaseUserRoleAccess>(context);
        ScheduledTasks = new Repository<ScheduledTask>(context);
        ScheduledTaskTriggers = new Repository<ScheduledTaskTrigger>(context);
        ScheduledTaskItems = new Repository<ScheduledTaskItem>(context);
        ScheduledTaskRuns = new Repository<ScheduledTaskRun>(context);
        ScheduledTaskViewers = new Repository<ScheduledTaskViewer>(context);
        ScheduledTaskViewerRoles = new Repository<ScheduledTaskViewerRole>(context);
        ScheduledTaskViewerUserGroups = new Repository<ScheduledTaskViewerUserGroup>(context);
        SystemTemplates = new Repository<SystemTemplate>(context);
        SystemAuditLogs = new Repository<SystemAuditLog>(context);
        SystemSettings = new Repository<SystemSetting>(context);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _context.Dispose();
            _disposed = true;
        }
    }
}
