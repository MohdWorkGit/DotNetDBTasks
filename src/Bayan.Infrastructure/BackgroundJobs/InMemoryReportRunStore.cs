using System.Collections.Concurrent;
using Bayan.Application.Common.Interfaces;
using Bayan.Infrastructure.Caching;
using Microsoft.Extensions.Configuration;

namespace Bayan.Infrastructure.BackgroundJobs;

/// <summary>
/// In-memory registry of report runs, singleton and single-instance only — the same constraint
/// <see cref="InMemoryQueryJobStore"/> documents, and for the same reason: a run is short-lived
/// interactive state whose rows live in this process's result cache.
///
/// <para>This holds no rows of its own. Each section's rows are a
/// <see cref="QueryJob"/> in <see cref="IQueryJobStore"/>, so every budget, spill and eviction
/// rule already written applies to report data without being restated here. What this adds is
/// the grouping: which section jobs belong to one run, so releasing a run releases all of them
/// rather than leaving orphans to age out one by one.</para>
/// </summary>
public class InMemoryReportRunStore : IReportRunStore
{
    private readonly ConcurrentDictionary<Guid, ReportRunEnvelope> _runs = new();
    private readonly IQueryJobStore _jobStore;
    private readonly TimeSpan _retention;

    public InMemoryReportRunStore(IQueryJobStore jobStore, IConfiguration configuration)
    {
        _jobStore = jobStore;
        // Same retention window as the cached results a run points at, read the same way, so a
        // run and its sections never expire on different clocks.
        _retention = ResultCacheOptions.FromConfiguration(configuration).Retention;
    }

    public ReportRunEnvelope Add(ReportRunEnvelope envelope)
    {
        envelope.LastAccessedAt = DateTime.UtcNow;
        _runs[envelope.Id] = envelope;
        return envelope;
    }

    public ReportRunEnvelope? Get(Guid id)
    {
        if (!_runs.TryGetValue(id, out var envelope))
            return null;

        envelope.LastAccessedAt = DateTime.UtcNow;

        // Retention on the section jobs is sliding, so touching them here keeps a run that is
        // being read alive as a whole. Without this a long reading session could lose one
        // section's rows while the rest survived.
        foreach (var section in envelope.Sections)
        {
            if (section.JobId is not null)
                _jobStore.Get(section.JobId.Value);
        }

        return envelope;
    }

    public void Remove(Guid id)
    {
        if (!_runs.TryRemove(id, out var envelope))
            return;

        foreach (var section in envelope.Sections)
        {
            if (section.JobId is not null)
                _jobStore.Remove(section.JobId.Value);
        }
    }

    public void RunMaintenance()
    {
        var cutoff = DateTime.UtcNow - _retention;
        foreach (var (id, envelope) in _runs.ToArray())
        {
            if (envelope.LastAccessedAt <= cutoff)
                Remove(id);
        }
    }
}
