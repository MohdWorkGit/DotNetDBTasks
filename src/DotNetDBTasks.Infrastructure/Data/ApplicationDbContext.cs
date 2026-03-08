using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetDBTasks.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context for the application.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<DynamicQuery> DynamicQueries => Set<DynamicQuery>();
    public DbSet<QueryParameter> QueryParameters => Set<QueryParameter>();
    public DbSet<DynamicQueryRole> DynamicQueryRoles => Set<DynamicQueryRole>();
    public DbSet<DynamicQueryDepartment> DynamicQueryDepartments => Set<DynamicQueryDepartment>();
    public DbSet<DynamicQueryUser> DynamicQueryUsers => Set<DynamicQueryUser>();
    public DbSet<QueryExecutionLog> QueryExecutionLogs => Set<QueryExecutionLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
