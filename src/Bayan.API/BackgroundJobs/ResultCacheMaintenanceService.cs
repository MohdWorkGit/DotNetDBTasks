using Bayan.Application.Common.Interfaces;

namespace Bayan.API.BackgroundJobs;

/// <summary>
/// Periodically drives <see cref="IQueryJobStore.RunMaintenance"/> so cached results are reclaimed
/// on a real schedule — evicting ones idle past the retention window and enforcing the heap/disk
/// size budgets — even when no new queries are being submitted. Without this, eviction would only
/// happen opportunistically when a new job is created and an idle server would never free memory.
///
/// <para>Also sweeps <see cref="IReportRunStore"/>, whose runs own section jobs: an idle report
/// run has to be released as a unit, otherwise its sections would age out one at a time and a
/// half-expired run would still look readable.</para>
/// </summary>
public class ResultCacheMaintenanceService : BackgroundService
{
    private readonly IQueryJobStore _jobStore;
    private readonly IReportRunStore _runStore;
    private readonly ILogger<ResultCacheMaintenanceService> _logger;

    public ResultCacheMaintenanceService(
        IQueryJobStore jobStore,
        IReportRunStore runStore,
        ILogger<ResultCacheMaintenanceService> logger)
    {
        _jobStore = jobStore;
        _runStore = runStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_jobStore.MaintenanceInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                // Report runs first: releasing an idle run frees its section jobs, so the
                // job-store sweep that follows sees the reclaimed budget in the same pass.
                _runStore.RunMaintenance();
                _jobStore.RunMaintenance();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Result cache maintenance sweep failed");
            }
        }
    }
}
