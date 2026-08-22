namespace Bayan.Application.Common.Interfaces;

/// <summary>One section of a completed report run, and the cached result holding its rows.</summary>
public sealed class ReportRunSection
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// The <see cref="QueryJob"/> holding this section's rows, or null when the section failed.
    /// Paging goes through the existing query-job endpoints against this id, so reports inherit
    /// the result cache's filter/sort semantics and its heap/disk spill behaviour unchanged.
    /// </summary>
    public Guid? JobId { get; init; }

    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    public int TotalRows { get; init; }
    public string? Error { get; init; }
    public bool IsVisibleInViewer { get; init; } = true;
}

/// <summary>A completed report run: which sections it produced, and what it ran with.</summary>
public sealed class ReportRunEnvelope
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid ReportId { get; init; }
    public string ReportName { get; init; } = string.Empty;
    public List<ReportRunSection> Sections { get; init; } = new();
    public List<Models.ExportParameter> Parameters { get; init; } = new();

    /// <summary>Charts built during the run, reused by the export and the viewer.</summary>
    public List<Models.ReportChartData> Charts { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public long ExecutionDurationMs { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastAccessedAt { get; set; }
}

/// <summary>
/// Ties one report run's section results together.
///
/// <para>Deliberately thin. The rows themselves stay in <see cref="IQueryJobStore"/> — one job
/// per section — so the existing spill-to-disk, LRU eviction, retention and cleanup all apply
/// unchanged, and the viewer pages sections through the endpoints that already exist. This
/// store only remembers which jobs belong to which run, which is what export and release need
/// and what nothing else can reconstruct.</para>
///
/// <para>Single-instance and in-memory, exactly like <c>InMemoryQueryJobStore</c>: a run is
/// short-lived interactive state, not a record. The durable record of a run is the
/// <c>ReportRuns</c> table.</para>
/// </summary>
public interface IReportRunStore
{
    ReportRunEnvelope Add(ReportRunEnvelope envelope);

    /// <summary>
    /// The run, if it is still held. Reading slides its retention <i>and</i> its sections',
    /// so a report being actively paged never half-expires.
    /// </summary>
    ReportRunEnvelope? Get(Guid id);

    /// <summary>Drops the run and every section job it owns, releasing their cached rows.</summary>
    void Remove(Guid id);

    /// <summary>Evicts runs idle beyond the retention window, releasing their section jobs.</summary>
    void RunMaintenance();
}
