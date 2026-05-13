using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetDBTasks.Infrastructure.Data;

/// <summary>
/// Seeds the database with initial roles, users, and an example dynamic query.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

        try
        {
            try
            {
                await context.Database.MigrateAsync();
            }
            catch (Exception ex) when (ContainsOracleError(ex, "ORA-00955", "ORA-01430"))
            {
                logger.LogWarning("Database objects already exist (ORA-00955/ORA-01430). Dropping all tables and recreating schema...");
                await DropAllTablesAsync(context);
                await context.Database.MigrateAsync();
            }

            // Repair broken schema: Oracle's non-transactional DDL can leave tables
            // without columns that EF Core expects (migration recorded as applied but
            // ALTER TABLE failed mid-way). See DATABASE_MIGRATION_NOTES.txt for details.
            await RepairDatabaseUsersSchemaAsync(context, logger);

            if (await context.Roles.CountAsync() > 0)
                return;

            logger.LogInformation("Seeding database...");

            // Seed Roles
            var adminRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = "Admin",
                Description = "System administrator with full access",
                CreatedAt = DateTime.UtcNow
            };

            var userRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = "User",
                Description = "Standard user with query execution access",
                CreatedAt = DateTime.UtcNow
            };

            var auditorRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = "Auditor",
                Description = "Auditor with access to execution logs and query accessibility management",
                CreatedAt = DateTime.UtcNow
            };

            context.Roles.AddRange(adminRole, userRole, auditorRole);

            // Seed Admin User (password: Admin@123)
            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "admin",
                Email = "admin@dotnetdbtasks.com",
                PasswordHash = passwordHasher.HashPassword("Admin@123"),
                FirstName = "System",
                LastName = "Administrator",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // Seed Regular User (password: User@123)
            var regularUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "user",
                Email = "user@dotnetdbtasks.com",
                PasswordHash = passwordHasher.HashPassword("User@123"),
                FirstName = "Regular",
                LastName = "User",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // Seed Auditor User (password: Auditor@123)
            var auditorUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "auditor",
                Email = "auditor@dotnetdbtasks.com",
                PasswordHash = passwordHasher.HashPassword("Auditor@123"),
                FirstName = "System",
                LastName = "Auditor",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            context.Users.AddRange(adminUser, regularUser, auditorUser);

            // Assign Roles
            context.UserRoles.AddRange(
                new UserRole { UserId = adminUser.Id, RoleId = adminRole.Id },
                new UserRole { UserId = regularUser.Id, RoleId = userRole.Id },
                new UserRole { UserId = auditorUser.Id, RoleId = auditorRole.Id }
            );

            // Seed Example Dynamic Query
            var exampleQuery = new DynamicQuery
            {
                Id = Guid.NewGuid(),
                Name = "Users by Registration Date",
                Description = "Retrieves users who registered between the specified date range.",
                SqlQuery = @"SELECT ""Username"",
       ""Email"",
       ""FirstName"",
       ""LastName"",
       ""CreatedAt""
  FROM ""Users""
 WHERE ""CreatedAt"" >= :StartDate
   AND ""CreatedAt"" <= :EndDate
   AND ""IsActive"" = :IsActive
 ORDER BY ""CreatedAt"" DESC",
                IsEnabled = true,
                TimeoutSeconds = 30,
                CreatedByUserId = adminUser.Id,
                CreatedAt = DateTime.UtcNow
            };

            exampleQuery.Parameters.Add(new QueryParameter
            {
                Id = Guid.NewGuid(),
                DynamicQueryId = exampleQuery.Id,
                Name = "StartDate",
                DisplayName = "Start Date",
                ParameterType = ParameterType.Date,
                IsRequired = true,
                SortOrder = 1,
                CreatedAt = DateTime.UtcNow
            });

            exampleQuery.Parameters.Add(new QueryParameter
            {
                Id = Guid.NewGuid(),
                DynamicQueryId = exampleQuery.Id,
                Name = "EndDate",
                DisplayName = "End Date",
                ParameterType = ParameterType.Date,
                IsRequired = true,
                SortOrder = 2,
                CreatedAt = DateTime.UtcNow
            });

            exampleQuery.Parameters.Add(new QueryParameter
            {
                Id = Guid.NewGuid(),
                DynamicQueryId = exampleQuery.Id,
                Name = "IsActive",
                DisplayName = "Active Only",
                ParameterType = ParameterType.Boolean,
                IsRequired = false,
                DefaultValue = "true",
                SortOrder = 3,
                CreatedAt = DateTime.UtcNow
            });

            context.DynamicQueries.Add(exampleQuery);

            // Assign the example query to both roles
            context.DynamicQueryRoles.AddRange(
                new DynamicQueryRole { DynamicQueryId = exampleQuery.Id, RoleId = adminRole.Id },
                new DynamicQueryRole { DynamicQueryId = exampleQuery.Id, RoleId = userRole.Id }
            );

            await context.SaveChangesAsync();
            logger.LogInformation("Database seeded successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    /// <summary>
    /// Repairs the DatabaseUsers table when migrations were recorded as applied but
    /// columns are missing due to Oracle's non-transactional DDL (auto-commit on each
    /// ALTER TABLE). This handles the permanently-broken state where:
    ///   1. AddMultiDatabaseSupport partially ran (some ALTERs committed, then one failed)
    ///   2. The migration was never recorded in __EFMigrationsHistory (or was recorded
    ///      despite partial failure in older seeder logic)
    ///   3. MigrateAsync() now skips it, leaving columns missing → ORA-00904
    /// Each statement is independently idempotent (catches ORA-01430 = column exists).
    /// </summary>
    private static async Task RepairDatabaseUsersSchemaAsync(ApplicationDbContext context, ILogger logger)
    {
        // Check if the DatabaseUsers table exists at all; if not, migrations will handle it.
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                @"SELECT 1 FROM ""DatabaseUsers"" WHERE ROWNUM = 0");
        }
        catch
        {
            return; // Table doesn't exist — nothing to repair.
        }

        var repairs = new (string column, string ddl)[]
        {
            ("ServerType",   @"ALTER TABLE ""DatabaseUsers"" ADD ""ServerType"" NUMBER(10) DEFAULT 0 NOT NULL"),
            ("DatabaseName", @"ALTER TABLE ""DatabaseUsers"" ADD ""DatabaseName"" NVARCHAR2(200) NULL"),
            ("ServiceName nullable", @"ALTER TABLE ""DatabaseUsers"" MODIFY ""ServiceName"" NULL"),
        };

        foreach (var (column, ddl) in repairs)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    $@"BEGIN EXECUTE IMMEDIATE '{ddl}'; EXCEPTION WHEN OTHERS THEN IF SQLCODE NOT IN (-1430, -1451) THEN RAISE; END IF; END;");
                logger.LogInformation("Schema repair: applied '{Column}' to DatabaseUsers.", column);
            }
            catch (Exception ex)
            {
                // -1430 = column already exists, -1451 = column already allows NULL
                // Any other error is unexpected but non-fatal for startup.
                logger.LogWarning(ex, "Schema repair: could not apply '{Column}' — may already be correct.", column);
            }
        }
    }

    /// <summary>
    /// Drops all application tables and __EFMigrationsHistory so MigrateAsync() can
    /// recreate the schema from scratch.
    /// </summary>
    private static async Task DropAllTablesAsync(ApplicationDbContext context)
    {
        var tablesToDrop = new[]
        {
            "DynamicQueryRoles", "DynamicQueryDepartments", "DynamicQueryUsers",
            "QueryExecutionLogs", "QueryParameters", "UserRoles",
            "DatabaseUserRoleAccess", "UserDatabaseUserAccess", "DynamicQueries", "DatabaseUsers",
            "Users", "Roles", "__EFMigrationsHistory"
        };
        foreach (var table in tablesToDrop)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    $@"BEGIN EXECUTE IMMEDIATE 'DROP TABLE ""{table}"" CASCADE CONSTRAINTS PURGE'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;");
            }
            catch { /* table may not exist */ }
        }
    }

    /// <summary>
    /// Checks if any exception in the chain contains one of the specified Oracle error codes.
    /// </summary>
    private static bool ContainsOracleError(Exception ex, params string[] oraCodes)
    {
        var current = ex;
        while (current != null)
        {
            foreach (var code in oraCodes)
            {
                if (current.Message.Contains(code))
                    return true;
            }
            current = current.InnerException;
        }
        return false;
    }
}
