using Jadwal.Application.Features.SystemAudit.Rotation;

namespace Jadwal.API.BackgroundJobs;

/// <summary>
/// Applies the log retention settings on a schedule: shortly after start-up, then every few
/// hours. The work itself is <see cref="AuditLogRotation"/>, which System Settings can also run
/// on demand; with both retentions at 0 (the default) each pass reads two settings and stops.
/// </summary>
public class AuditLogRotationService : BackgroundService
{
    /// <summary>Long enough after start-up not to compete with migrations and the first requests.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    /// <summary>Retention is counted in days, so a few passes a day keep it within hours of exact.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogRotationService> _logger;

    public AuditLogRotationService(IServiceScopeFactory scopeFactory, ILogger<AuditLogRotationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var rotation = scope.ServiceProvider.GetRequiredService<AuditLogRotation>();
                var result = await rotation.RotateAsync(stoppingToken);

                if (result.ExecutionLogsDeleted > 0 || result.SystemLogsDeleted > 0)
                {
                    _logger.LogInformation(
                        "Log rotation removed {ExecutionLogs} execution log(s) and {SystemLogs} system audit entr(ies).",
                        result.ExecutionLogsDeleted, result.SystemLogsDeleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Log rotation failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
