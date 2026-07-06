using DotNetDBTasks.API.BackgroundJobs;
using DotNetDBTasks.API.Extensions;
using DotNetDBTasks.API.Middleware;
using DotNetDBTasks.Application;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Infrastructure;
using DotNetDBTasks.Infrastructure.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog structured logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// No-op when run from a console or under IIS; when installed as a Windows service
// (sc.exe create ... binPath=DotNetDBTasks.API.exe) it wires up service lifetime
// events and sets the working directory so relative paths (logs/, wwwroot) resolve.
builder.Host.UseWindowsService();

// Clean Architecture layer registration
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// HTTP context accessor for current user service
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Background worker that executes submitted query jobs off the request thread
builder.Services.AddHostedService<QueryJobWorker>();

// Background worker that triggers scheduled export tasks and manual "run now" requests
builder.Services.AddHostedService<ScheduledTaskWorker>();

// Controllers
builder.Services.AddControllers();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? new[] { "http://localhost:4200" })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DotNetDBTasks API",
        Version = "v1",
        Description = "Dynamic Database Query Execution Platform"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Forward headers from reverse proxy (Cloudflare / nginx)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Serve the Angular SPA from wwwroot (single-site IIS hosting). Static assets are
// served directly; unmatched non-API routes fall back to index.html further below.
app.UseDefaultFiles();
app.UseStaticFiles();

// Global exception handling middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger (available in all environments for API documentation)
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "DotNetDBTasks API v1"));

app.UseCors("AllowAngular");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// SPA fallback: any request that isn't an API route or a physical file returns
// index.html so Angular client-side routing (deep links, refresh) works.
app.MapFallbackToFile("index.html");

// Database migration and seeding
await DatabaseSeeder.SeedAsync(app.Services);

Log.Information("DotNetDBTasks API started successfully.");
app.Run();
