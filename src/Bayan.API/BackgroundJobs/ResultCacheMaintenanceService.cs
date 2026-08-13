using Bayan.Application.Common.Interfaces;

namespace Bayan.API.BackgroundJobs;

/// <summary>
/// Periodically drives <see cref="IQueryJobStore.RunMaintenance"/> so cached results are reclaimed
/// on a real schedule — evicting ones idle past the retention window and enforcing the heap/disk
/// size budgets — even when no new queries are being submitted. Without this, eviction would only
/// happen opportunistically when a new job is created and an idle server would never free memory.
/// </summary>
public class ResultCacheMaintenanceService : BackgroundService
{
    private readonly IQueryJobStore _jobStore;
    private readonly ILogger<ResultCacheMaintenanceService> _logger;

    public ResultCacheMaintenanceService(
        IQueryJobStore jobStore,
        ILogger<ResultCacheMaintenanceService> logger)
    {
        _jobStore = jobStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_jobStore.MaintenanceInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                _jobStore.RunMaintenance();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Result cache maintenance sweep failed");
            }
        }
    }
}
