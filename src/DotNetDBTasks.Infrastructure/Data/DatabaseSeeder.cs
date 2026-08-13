using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Constants;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

        // The drop-all-and-recreate fallback below destroys every table and all data. It is
        // only acceptable on a disposable dev database, so it must be enabled explicitly via
        // Database:AllowDestructiveRepair — production/isolated machines keep the default (off)
        // and fail loudly instead, leaving the schema for a manual/DBA repair.
        var allowDestructiveRepair = scope.ServiceProvider
            .GetRequiredService<IConfiguration>()
            .GetValue("Database:AllowDestructiveRepair", false);

        try
        {
            try
            {
                await context.Database.MigrateAsync();
            }
            catch (Exception ex) when (ContainsOracleError(ex, "ORA-00955", "ORA-01430"))
            {
                if (!allowDestructiveRepair)
                {
                    logger.LogError(ex,
                        "Migration failed because database objects already exist (ORA-00955/ORA-01430), " +
                        "usually after a partially applied migration. Destructive repair is disabled " +
                        "(Database:AllowDestructiveRepair=false); repair the schema manually — e.g. apply " +
                        "the idempotent script from 'dotnet ef migrations script --idempotent' — and restart.");
                    throw;
                }

                logger.LogWarning("Database objects already exist (ORA-00955/ORA-01430). " +
                    "Database:AllowDestructiveRepair is enabled — dropping all tables and recreating schema...");
                await DropAllTablesAsync(context);
                await context.Database.MigrateAsync();
            }

            // Repair broken schema: Oracle's non-transactional DDL can leave tables
            // without columns that EF Core expects (migration recorded as applied but
            // ALTER TABLE failed mid-way). See DATABASE_MIGRATION_NOTES.txt for details.
            await RepairDatabaseUsersSchemaAsync(context, logger);

            if (await context.Roles.CountAsync() > 0)
            {
                // Roles introduced after the first release still have to reach databases
                // seeded by an earlier version — the full seed below is skipped for those.
                await EnsureRolesExistAsync(context, logger);
                await EnsureSeededRolePermissionsAsync(context, logger);
                return;
            }

            logger.LogInformation("Seeding database...");

            // Seed Roles
            var adminRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = RoleNames.Admin,
                Description = "System administrator with full access",
                CreatedAt = DateTime.UtcNow
            };

            var userRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = RoleNames.User,
                Description = "Standard user with query execution access",
                CreatedAt = DateTime.UtcNow
            };

            var auditorRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = RoleNames.Auditor,
                Description = AuditorRoleDescription,
                CreatedAt = DateTime.UtcNow
            };

            var accessManagerRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = RoleNames.AccessManager,
                Description = AccessManagerRoleDescription,
                CreatedAt = DateTime.UtcNow
            };

            foreach (var role in new[] { adminRole, userRole, auditorRole, accessManagerRole })
                role.IsSeeded = true;

            context.Roles.AddRange(adminRole, userRole, auditorRole, accessManagerRole);
            context.RolePermissions.AddRange(DefaultPermissionsFor(
                new[] { adminRole, userRole, auditorRole, accessManagerRole }));

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

            // Seed Access Manager User (password: Access@123)
            var accessManagerUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "accessmanager",
                Email = "accessmanager@dotnetdbtasks.com",
                PasswordHash = passwordHasher.HashPassword("Access@123"),
                FirstName = "Access",
                LastName = "Manager",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            context.Users.AddRange(adminUser, regularUser, auditorUser, accessManagerUser);

            // Assign Roles
            context.UserRoles.AddRange(
                new UserRole { UserId = adminUser.Id, RoleId = adminRole.Id },
                new UserRole { UserId = regularUser.Id, RoleId = userRole.Id },
                new UserRole { UserId = auditorUser.Id, RoleId = auditorRole.Id },
                new UserRole { UserId = accessManagerUser.Id, RoleId = accessManagerRole.Id }
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
            exampleQuery.QueryType = QueryTypeClassifier.FromSql(exampleQuery.SqlQuery);

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

    private const string AuditorRoleDescription =
        "Auditor with read access to execution logs and scheduled task history";

    private const string AccessManagerRoleDescription =
        "Manages which roles, user groups and users may access queries and query groups";

    /// <summary>
    /// Descriptions shipped by earlier versions whose wording no longer matches what the role
    /// can do. A row still carrying one of these verbatim has never been edited, so replacing
    /// it corrects the text without overwriting anything an operator wrote themselves.
    /// </summary>
    private static readonly Dictionary<string, string[]> SupersededDescriptions = new()
    {
        [RoleNames.Auditor] = new[]
        {
            // Pre-dates the split that moved accessibility management to AccessManager.
            "Auditor with access to execution logs and query accessibility management"
        },
        [RoleNames.AccessManager] = new[]
        {
            // Pre-dates access moving off AD departments onto app-owned user groups.
            "Manages which roles, departments and users may access queries and query groups"
        }
    };

    /// <summary>
    /// Inserts any seeded role missing from an already-populated database, and refreshes
    /// descriptions still carrying superseded wording. Any other existing text is left alone,
    /// so an operator's edits survive a restart. No user is attached to a backfilled role —
    /// an administrator assigns it.
    /// </summary>
    private static async Task EnsureRolesExistAsync(ApplicationDbContext context, ILogger logger)
    {
        var expected = new (string Name, string Description)[]
        {
            (RoleNames.Admin, "System administrator with full access"),
            (RoleNames.User, "Standard user with query execution access"),
            (RoleNames.Auditor, AuditorRoleDescription),
            (RoleNames.AccessManager, AccessManagerRoleDescription)
        };

        var existing = await context.Roles.ToListAsync();
        var existingNames = existing.Select(r => r.Name).ToHashSet();

        var missing = expected
            .Where(e => !existingNames.Contains(e.Name))
            .Select(e => new Role
            {
                Id = Guid.NewGuid(),
                Name = e.Name,
                Description = e.Description,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        var reworded = new List<string>();
        foreach (var (name, description) in expected)
        {
            var role = existing.FirstOrDefault(r => r.Name == name);
            if (role?.Description is null
                || !SupersededDescriptions.TryGetValue(name, out var stale)
                || !stale.Contains(role.Description))
                continue;

            role.Description = description;
            reworded.Add(name);
        }

        if (missing.Count == 0 && reworded.Count == 0)
            return;

        context.Roles.AddRange(missing);
        await context.SaveChangesAsync();

        if (missing.Count > 0)
            logger.LogInformation("Added missing role(s): {Roles}.",
                string.Join(", ", missing.Select(r => r.Name)));
        if (reworded.Count > 0)
            logger.LogInformation("Refreshed superseded description(s) for role(s): {Roles}.",
                string.Join(", ", reworded));
    }

    /// <summary>
    /// The permission rows a set of seeded roles starts with. Admin is skipped: it is pinned to
    /// every permission in code, so storing them would only let the two drift apart.
    /// </summary>
    private static IEnumerable<RolePermission> DefaultPermissionsFor(IEnumerable<Role> roles) =>
        roles.Where(r => Permissions.SeededDefaults.ContainsKey(r.Name))
             .SelectMany(r => Permissions.SeededDefaults[r.Name]
                 .Select(p => new RolePermission { RoleId = r.Id, Permission = p }));

    /// <summary>
    /// Gives a seeded role its default permissions when it has none at all.
    ///
    /// <para>Only when it has none: a role an administrator has deliberately stripped back must
    /// stay stripped back, and re-adding "just the missing ones" on every start would make a
    /// removed permission impossible to remove. The migration covers databases that already had
    /// these roles; this covers a role backfilled later by <c>EnsureRolesExistAsync</c>.</para>
    /// </summary>
    private static async Task EnsureSeededRolePermissionsAsync(ApplicationDbContext context, ILogger logger)
    {
        var seededNames = Permissions.SeededDefaults.Keys.ToList();
        var roles = await context.Roles
            .Where(r => seededNames.Contains(r.Name))
            .Include(r => r.RolePermissions)
            .ToListAsync();

        var bare = roles.Where(r => r.RolePermissions.Count == 0).ToList();

        // Mark the seeded four, for databases migrated before the column existed.
        var unflagged = await context.Roles
            .Where(r => !r.IsSeeded && (seededNames.Contains(r.Name) || r.Name == RoleNames.Admin))
            .ToListAsync();
        foreach (var role in unflagged)
            role.IsSeeded = true;

        if (bare.Count == 0 && unflagged.Count == 0)
            return;

        context.RolePermissions.AddRange(DefaultPermissionsFor(bare));
        await context.SaveChangesAsync();

        if (bare.Count > 0)
            logger.LogInformation("Applied default permissions to role(s): {Roles}.",
                string.Join(", ", bare.Select(r => r.Name)));
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
            "DynamicQueryRoles", "DynamicQueryUserGroups", "DynamicQueryUsers",
            "QueryExecutionLogs", "QueryParameters", "UserRoles", "UserGroupMembers", "UserGroups",
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
