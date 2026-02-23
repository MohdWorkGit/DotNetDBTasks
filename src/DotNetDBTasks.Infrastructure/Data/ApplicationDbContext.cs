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
    public DbSet<QueryExecutionLog> QueryExecutionLogs => Set<QueryExecutionLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Oracle PL/SQL cannot bind DbType.Boolean parameters to NUMBER(1) columns
        // inside DECLARE/BEGIN...END blocks. Convert all bool properties to int (0/1)
        // so EF Core sends DbType.Int32 instead of DbType.Boolean.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(bool))
                {
                    property.SetValueConverter(
                        new Microsoft.EntityFrameworkCore.Storage.ValueConversion.BoolToZeroOneConverter<int>());
                }
            }
        }
    }
}
