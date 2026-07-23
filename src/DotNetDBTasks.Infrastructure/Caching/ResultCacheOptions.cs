using Microsoft.Extensions.Configuration;

namespace DotNetDBTasks.Infrastructure.Caching;

/// <summary>
/// Tunables for the result cache, bound from the "ResultCache" configuration section. Controls how
/// long results are retained, how often maintenance runs, when a result spills to disk, and the
/// heap/disk budgets that trigger LRU eviction. All values fall back to sensible defaults.
/// </summary>
public sealed class ResultCacheOptions
{
    /// <summary>How long a result may sit idle (untouched) before it is evicted.</summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromMinutes(1440);

    /// <summary>How often the background maintenance sweep runs.</summary>
    public TimeSpan MaintenanceInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Results whose serialized size exceeds this spill to disk instead of the heap.</summary>
    public long SpillThresholdBytes { get; init; } = 5L * 1024 * 1024;

    /// <summary>Budget for in-memory cached results; the oldest are evicted once exceeded.</summary>
    public long MaxHeapBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Budget for on-disk spilled results; the oldest are evicted once exceeded.</summary>
    public long MaxDiskBytes { get; init; } = 5L * 1024 * 1024 * 1024;

    /// <summary>Directory that holds the spill files (created on demand, cleared at startup/shutdown).</summary>
    public string SpillDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "cache", "results");

    public static ResultCacheOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("ResultCache");

        var retentionMinutes = section.GetValue("RetentionMinutes", 1440);
        if (retentionMinutes <= 0) retentionMinutes = 1440;

        var maintenanceSeconds = section.GetValue("MaintenanceIntervalSeconds", 60);
        if (maintenanceSeconds <= 0) maintenanceSeconds = 60;

        var spillThreshold = section.GetValue("SpillThresholdBytes", 5L * 1024 * 1024);
        if (spillThreshold <= 0) spillThreshold = 5L * 1024 * 1024;

        var maxHeap = section.GetValue("MaxHeapBytes", 256L * 1024 * 1024);
        if (maxHeap <= 0) maxHeap = 256L * 1024 * 1024;

        var maxDisk = section.GetValue("MaxDiskBytes", 5L * 1024 * 1024 * 1024);
        if (maxDisk <= 0) maxDisk = 5L * 1024 * 1024 * 1024;

        var directory = section.GetValue<string>("SpillDirectory");
        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.Combine(AppContext.BaseDirectory, "cache", "results");

        return new ResultCacheOptions
        {
            Retention = TimeSpan.FromMinutes(retentionMinutes),
            MaintenanceInterval = TimeSpan.FromSeconds(maintenanceSeconds),
            SpillThresholdBytes = spillThreshold,
            MaxHeapBytes = maxHeap,
            MaxDiskBytes = maxDisk,
            SpillDirectory = directory
        };
    }
}
