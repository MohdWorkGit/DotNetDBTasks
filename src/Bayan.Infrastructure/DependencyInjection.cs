using System.Text;
using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Interfaces;
using Bayan.Infrastructure.BackgroundJobs;
using Bayan.Infrastructure.Data;
using Bayan.Infrastructure.Identity;
using Bayan.Infrastructure.Repositories;
using Bayan.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Bayan.Infrastructure;

/// <summary>
/// Registers Infrastructure layer services into the DI container.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Entity Framework Core - Oracle
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseOracle(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        // Repositories
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Services
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IQueryExecutor, QueryExecutor>();
        services.AddScoped<IDatabaseConnectionFactory, DatabaseConnectionFactory>();
        services.AddScoped<IEncryptionService, AesEncryptionService>();
        services.AddScoped<ILdapService, LdapService>();
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddSingleton<IExcelExporter, ExcelExporter>();
        services.AddSingleton<IDocxToPdfConverter, DocxToPdfConverter>();
        services.AddSingleton<IResultFileExporter, ResultFileExporter>();

        // Scheduled export tasks: manual/scheduled runs flow through a shared queue
        // drained by the ScheduledTaskWorker; each run executes on a scoped runner. The run
        // registry (singleton) lets the cancel endpoint stop an in-flight run.
        services.AddSingleton<IScheduledTaskRunQueue, ScheduledTaskRunQueue>();
        services.AddSingleton<IScheduledTaskRunRegistry, ScheduledTaskRunRegistry>();
        services.AddScoped<IScheduledTaskRunner, ScheduledTaskRunner>();

        // Async query execution: in-memory job store + queue + ambient user context.
        // Singletons so they are shared across requests and the background worker.
        services.AddSingleton<IQueryJobStore, InMemoryQueryJobStore>();
        services.AddSingleton<IQueryJobQueue, QueryJobQueue>();
        services.AddSingleton<IUserExecutionContext, UserExecutionContext>();

        // JWT Authentication
        var jwtSecret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT:Secret is not configured.");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ValidateIssuer = true,
                ValidIssuer = configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        })
        .AddNegotiate();

        services.AddAuthorization();

        return services;
    }
}
