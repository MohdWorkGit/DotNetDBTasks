using DotNetDBTasks.API.BackgroundJobs;
using DotNetDBTasks.API.Extensions;
using DotNetDBTasks.API.Middleware;
using DotNetDBTasks.Application;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Infrastructure;
using DotNetDBTasks.Infrastructure.Data;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
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
builder.Services.AddScoped<IAppLocalizer, AppLocalizer>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();

// Background worker that executes submitted query jobs off the request thread
builder.Services.AddHostedService<QueryJobWorker>();

// Background worker that reclaims cached results (idle eviction + heap/disk size budgets)
builder.Services.AddHostedService<ResultCacheMaintenanceService>();

// Background worker that triggers scheduled export tasks and manual "run now" requests
builder.Services.AddHostedService<ScheduledTaskWorker>();

// Controllers
builder.Services.AddControllers();

// Localization. Messages the client displays verbatim (validation failures, permission
// denials, AD import summaries) live in Resources/Messages.*.resx in the Application project.
// ResourcesPath must match the folder holding Messages.resx in the Application project;
// the lookup is by convention, so a mismatch degrades silently to raw keys.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supported = new[] { new CultureInfo("en"), new CultureInfo("ar") };
    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = supported;
    options.SupportedUICultures = supported;

    // The Angular client sends Accept-Language on every request (language.interceptor.ts).
    // Query-string and cookie providers are dropped: the header is the single source of
    // truth, so a stale cookie cannot outrank the language the user is actually looking at.
    options.RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new AcceptLanguageHeaderRequestCultureProvider()
    };
});

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

// Rate limiting — throttles password-based login to blunt brute-force / credential-stuffing.
// Partitioned by client IP (the real caller once ForwardedHeaders has run), so an attacker
// hammering the endpoint from one source is capped while normal users are unaffected.
var loginPermitLimit = builder.Configuration.GetValue("Auth:Login:PermitLimit", 10);
var loginWindowSeconds = builder.Configuration.GetValue("Auth:Login:WindowSeconds", 60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermitLimit,
            Window = TimeSpan.FromSeconds(loginWindowSeconds),
            QueueLimit = 0
        });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"message\":\"Too many login attempts. Please wait and try again.\"}", cancellationToken);
    };
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

// Before the exception middleware: a localized message is only produced if the request
// culture is already set by the time a handler throws.
app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);

// Serve the Angular SPA from wwwroot (single-site IIS hosting). Static assets are
// served directly; unmatched non-API routes fall back to index.html further below.
app.UseDefaultFiles();
app.UseStaticFiles();

// Global exception handling middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger documents the entire API surface, so it is only served outside Production
// (local dev/testing). In Production these endpoints return 404.
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "DotNetDBTasks API v1"));
}

app.UseRouting();
app.UseRateLimiter();
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
