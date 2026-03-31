using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Infrastructure.Services;

/// <summary>
/// Builds connection strings and provides test queries for Oracle, SQL Server, PostgreSQL, and MySQL.
/// </summary>
public class DatabaseConnectionFactory : IDatabaseConnectionFactory
{
    public string BuildConnectionString(DatabaseUser dbUser, string password)
    {
        return dbUser.ServerType switch
        {
            DatabaseServerType.Oracle =>
                $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={dbUser.Host})(PORT={dbUser.Port}))(CONNECT_DATA=(SERVICE_NAME={dbUser.ServiceName ?? string.Empty})));User Id={dbUser.DbUsername};Password={password};",

            DatabaseServerType.SqlServer =>
                $"Server={dbUser.Host},{dbUser.Port};Database={dbUser.DatabaseName ?? string.Empty};User Id={dbUser.DbUsername};Password={password};TrustServerCertificate=True;",

            DatabaseServerType.PostgreSql =>
                $"Host={dbUser.Host};Port={dbUser.Port};Database={dbUser.DatabaseName ?? string.Empty};Username={dbUser.DbUsername};Password={password};",

            DatabaseServerType.MySql =>
                $"Server={dbUser.Host};Port={dbUser.Port};Database={dbUser.DatabaseName ?? string.Empty};User={dbUser.DbUsername};Password={password};",

            _ => throw new NotSupportedException($"Database server type '{dbUser.ServerType}' is not supported.")
        };
    }

    public string GetTestQuery(DatabaseUser dbUser)
    {
        return dbUser.ServerType switch
        {
            DatabaseServerType.Oracle => "SELECT 1 FROM DUAL",
            _ => "SELECT 1"
        };
    }
}
