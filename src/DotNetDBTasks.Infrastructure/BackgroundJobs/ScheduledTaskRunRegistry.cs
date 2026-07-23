using System.Collections.Concurrent;
using DotNetDBTasks.Application.Common.Interfaces;

namespace DotNetDBTasks.Infrastructure.BackgroundJobs;

/// <summary>
/// In-memory <see cref="IScheduledTaskRunRegistry"/> backed by a ConcurrentDictionary. Registered as
/// a singleton so the runner (background worker) and the cancel endpoint share the same map of
/// in-flight runs.
/// </summary>
public class ScheduledTaskRunRegistry : IScheduledTaskRunRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runs = new();

    public CancellationTokenSource Register(Guid runId, CancellationToken linkedToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
        _runs[runId] = cts;
        return cts;
    }

    public void Unregister(Guid runId)
    {
        if (_runs.TryRemove(runId, out var cts))
            cts.Dispose();
    }

    public bool Cancel(Guid runId)
    {
        if (!_runs.TryGetValue(runId, out var cts))
            return false;

        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The run finished and disposed its source between the lookup and the cancel.
            return false;
        }
    }
}
