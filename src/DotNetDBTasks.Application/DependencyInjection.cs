using System.Reflection;
using DotNetDBTasks.Application.Common.Behaviors;
using DotNetDBTasks.Application.Common.Security;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetDBTasks.Application;

/// <summary>
/// Registers Application layer services into the DI container.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(Assembly.GetExecutingAssembly());
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            // After validation on purpose: a command rejected by a validator never executed,
            // so recording it would fill the audit trail with typos rather than actions.
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuditLoggingBehavior<,>));
        });

        // Scoped: it reads the current request's user and the per-request unit of work.
        services.AddScoped<AdminAccountGuard>();

        return services;
    }
}
