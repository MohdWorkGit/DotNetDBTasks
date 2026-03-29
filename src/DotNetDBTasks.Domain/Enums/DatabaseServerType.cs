namespace DotNetDBTasks.Domain.Enums;

/// <summary>
/// Identifies the type of database server for a database user connection.
/// </summary>
public enum DatabaseServerType
{
    Oracle = 0,
    SqlServer = 1,
    PostgreSql = 2,
    MySql = 3
}
