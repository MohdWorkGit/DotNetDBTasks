using System.Threading.Channels;
using Bayan.Application.Common.Interfaces;

namespace Bayan.Infrastructure.BackgroundJobs;

/// <summary>
/// Bounded channel-backed implementation of <see cref="IScheduledTaskRunQueue"/>.
/// Registered as a singleton. When the queue is full, producers wait rather than
/// dropping runs.
/// </summary>
public class ScheduledTaskRunQueue : IScheduledTaskRunQueue
{
    private readonly Channel<ScheduledTaskRunRequest> _channel = Channel.CreateBounded<ScheduledTaskRunRequest>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    public ValueTask EnqueueAsync(ScheduledTaskRunRequest request, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(request, cancellationToken);

    public IAsyncEnumerable<ScheduledTaskRunRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
