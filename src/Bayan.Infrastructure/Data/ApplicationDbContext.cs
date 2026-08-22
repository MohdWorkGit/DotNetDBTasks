using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bayan.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context for the application.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<UserGroupMember> UserGroupMembers => Set<UserGroupMember>();
    public DbSet<DynamicQuery> DynamicQueries => Set<DynamicQuery>();
    public DbSet<QueryParameter> QueryParameters => Set<QueryParameter>();
    public DbSet<DynamicQueryRole> DynamicQueryRoles => Set<DynamicQueryRole>();
    public DbSet<DynamicQueryUserGroup> DynamicQueryUserGroups => Set<DynamicQueryUserGroup>();
    public DbSet<DynamicQueryUser> DynamicQueryUsers => Set<DynamicQueryUser>();
    public DbSet<QueryGroup> QueryGroups => Set<QueryGroup>();
    public DbSet<QueryGroupRole> QueryGroupRoles => Set<QueryGroupRole>();
    public DbSet<QueryGroupUserGroup> QueryGroupUserGroups => Set<QueryGroupUserGroup>();
    public DbSet<QueryGroupUser> QueryGroupUsers => Set<QueryGroupUser>();
    public DbSet<QueryExecutionLog> QueryExecutionLogs => Set<QueryExecutionLog>();
    public DbSet<DatabaseUser> DatabaseUsers => Set<DatabaseUser>();
    public DbSet<DatabaseUserRoleAccess> DatabaseUserRoleAccess => Set<DatabaseUserRoleAccess>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<ScheduledTaskTrigger> ScheduledTaskTriggers => Set<ScheduledTaskTrigger>();
    public DbSet<ScheduledTaskItem> ScheduledTaskItems => Set<ScheduledTaskItem>();
    public DbSet<ScheduledTaskRun> ScheduledTaskRuns => Set<ScheduledTaskRun>();
    public DbSet<ScheduledTaskViewer> ScheduledTaskViewers => Set<ScheduledTaskViewer>();
    public DbSet<ScheduledTaskViewerRole> ScheduledTaskViewerRoles => Set<ScheduledTaskViewerRole>();
    public DbSet<ScheduledTaskViewerUserGroup> ScheduledTaskViewerUserGroups => Set<ScheduledTaskViewerUserGroup>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportDataset> ReportDatasets => Set<ReportDataset>();
    public DbSet<ReportParameter> ReportParameters => Set<ReportParameter>();
    public DbSet<ReportParameterMap> ReportParameterMaps => Set<ReportParameterMap>();
    public DbSet<ReportRole> ReportRoles => Set<ReportRole>();
    public DbSet<ReportUserGroup> ReportUserGroups => Set<ReportUserGroup>();
    public DbSet<ReportUser> ReportUsers => Set<ReportUser>();
    public DbSet<ReportChart> ReportCharts => Set<ReportChart>();
    public DbSet<ReportRun> ReportRuns => Set<ReportRun>();
    public DbSet<SystemTemplate> SystemTemplates => Set<SystemTemplate>();
    public DbSet<SystemAuditLog> SystemAuditLogs => Set<SystemAuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Oracle does not support boolean literals (TRUE/FALSE).
        // Convert all bool properties to NUMBER(1) with 1/0 values.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(bool) || property.ClrType == typeof(bool?))
                {
                    property.SetValueConverter(
                        new Microsoft.EntityFrameworkCore.Storage.ValueConversion.BoolToZeroOneConverter<short>());
                }
            }
        }
    }
}
