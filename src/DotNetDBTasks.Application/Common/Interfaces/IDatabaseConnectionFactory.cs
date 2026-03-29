using DotNetDBTasks.Domain.Entities;

namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Builds connection strings and provides test queries for different database server types.
/// </summary>
public interface IDatabaseConnectionFactory
{
    /// <summary>
    /// Builds a connection string from a DatabaseUser entity and a decrypted password.
    /// </summary>
    string BuildConnectionString(DatabaseUser dbUser, string password);

    /// <summary>
    /// Returns a simple test query appropriate for the database server type
    /// (e.g. "SELECT 1 FROM DUAL" for Oracle, "SELECT 1" for others).
    /// </summary>
    string GetTestQuery(DatabaseUser dbUser);
}
