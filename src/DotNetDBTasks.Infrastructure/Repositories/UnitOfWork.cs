using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Interfaces;
using DotNetDBTasks.Infrastructure.Data;

namespace DotNetDBTasks.Infrastructure.Repositories;

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
    public IRepository<DynamicQueryRole> DynamicQueryRoles { get; }
    public IRepository<DynamicQueryDepartment> DynamicQueryDepartments { get; }
    public IRepository<DynamicQueryUser> DynamicQueryUsers { get; }
    public IRepository<QueryGroup> QueryGroups { get; }
    public IRepository<QueryGroupRole> QueryGroupRoles { get; }
    public IRepository<QueryGroupDepartment> QueryGroupDepartments { get; }
    public IRepository<QueryGroupUser> QueryGroupUsers { get; }
    public IRepository<DatabaseUser> DatabaseUsers { get; }
    public IRepository<DatabaseUserRoleAccess> DatabaseUserRoleAccess { get; }

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
        Users = new Repository<User>(context);
        Roles = new Repository<Role>(context);
        DynamicQueries = new Repository<DynamicQuery>(context);
        QueryParameters = new Repository<QueryParameter>(context);
        QueryExecutionLogs = new Repository<QueryExecutionLog>(context);
        UserRoles = new Repository<UserRole>(context);
        DynamicQueryRoles = new Repository<DynamicQueryRole>(context);
        DynamicQueryDepartments = new Repository<DynamicQueryDepartment>(context);
        DynamicQueryUsers = new Repository<DynamicQueryUser>(context);
        QueryGroups = new Repository<QueryGroup>(context);
        QueryGroupRoles = new Repository<QueryGroupRole>(context);
        QueryGroupDepartments = new Repository<QueryGroupDepartment>(context);
        QueryGroupUsers = new Repository<QueryGroupUser>(context);
        DatabaseUsers = new Repository<DatabaseUser>(context);
        DatabaseUserRoleAccess = new Repository<DatabaseUserRoleAccess>(context);
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
