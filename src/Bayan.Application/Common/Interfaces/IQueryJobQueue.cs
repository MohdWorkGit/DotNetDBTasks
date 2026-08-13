namespace Bayan.Application.Common.Interfaces;

/// <summary>
/// Bounded producer/consumer queue of pending query job ids. The controller enqueues
/// after creating a job; the background worker drains it.
/// </summary>
public interface IQueryJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);
}
