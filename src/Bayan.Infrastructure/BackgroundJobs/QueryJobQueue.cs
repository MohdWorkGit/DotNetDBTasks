using System.Threading.Channels;
using Bayan.Application.Common.Interfaces;

namespace Bayan.Infrastructure.BackgroundJobs;

/// <summary>
/// Bounded channel-backed implementation of <see cref="IQueryJobQueue"/>. Registered as
/// a singleton. When the queue is full, producers wait rather than dropping jobs.
/// </summary>
public class QueryJobQueue : IQueryJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
