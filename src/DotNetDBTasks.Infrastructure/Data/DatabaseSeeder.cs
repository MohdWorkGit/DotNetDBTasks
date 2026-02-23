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
            catch (Exception ex) when (ex.Message.Contains("ORA-00955") || ex.InnerException?.Message?.Contains("ORA-00955") == true)
            {
                logger.LogWarning("Database objects already exist (ORA-00955). Dropping all tables and recreating schema...");

                // Drop application tables explicitly (user_tables includes Oracle system tables like LogMiner that can't be dropped)
                var tablesToDrop = new[] { "DynamicQueryRoles", "QueryExecutionLogs", "QueryParameters", "UserRoles", "DynamicQueries", "Users", "Roles", "__EFMigrationsHistory" };
                foreach (var table in tablesToDrop)
                {
                    try
                    {
                        await context.Database.ExecuteSqlRawAsync($@"BEGIN EXECUTE IMMEDIATE 'DROP TABLE ""{table}"" CASCADE CONSTRAINTS PURGE'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;");
                    }
                    catch { /* table may not exist */ }
                }

                await context.Database.MigrateAsync();
            }

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

            context.Roles.AddRange(adminRole, userRole);

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

            context.Users.AddRange(adminUser, regularUser);

            // Assign Roles
            context.UserRoles.AddRange(
                new UserRole { UserId = adminUser.Id, RoleId = adminRole.Id },
                new UserRole { UserId = regularUser.Id, RoleId = userRole.Id }
            );

            // Seed Example Dynamic Query
            var exampleQuery = new DynamicQuery
            {
                Id = Guid.NewGuid(),
                Name = "Users by Registration Date",
                Description = "Retrieves users who registered between the specified date range.",
                SqlQuery = "SELECT \"Username\", \"Email\", \"FirstName\", \"LastName\", \"CreatedAt\" FROM \"Users\" WHERE \"CreatedAt\" >= :StartDate AND \"CreatedAt\" <= :EndDate AND \"IsActive\" = :IsActive ORDER BY \"CreatedAt\" DESC",
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
}
