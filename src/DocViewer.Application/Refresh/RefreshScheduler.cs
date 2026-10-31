using System.Collections.Concurrent;
using System.Threading.Channels;
using DocViewer.Application.Observability;
using DocViewer.Application.Options;
using DocViewer.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocViewer.Application.Refresh;

/// <summary>
/// In-process queue of VINs to refresh in the background (D-033). A VIN is queued once until its job finishes;
/// each attempt is released to the reader only when its delay has passed. Jobs live in memory: a restart drops them,
/// and the next partial search queues the VIN again.
/// </summary>
public sealed partial class RefreshScheduler(
    IOptions<RefreshOptions> options,
    TimeProvider timeProvider,
    DocViewerMetrics metrics,
    ILogger<RefreshScheduler> logger) : IDisposable
{
    private readonly Channel<RefreshJob> _due = Channel.CreateUnbounded<RefreshJob>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Vin, byte> _pending = new();
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>VINs queued or being refreshed.</summary>
    public int Pending => _pending.Count;

    /// <summary>Queues <paramref name="vin"/> for its first attempt; <c>false</c> when disabled, already queued or full.</summary>
    public bool TryEnqueue(Vin vin)
    {
        ArgumentNullException.ThrowIfNull(vin);
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return false;
        }

        if (_pending.Count >= settings.QueueCapacity)
        {
            metrics.RefreshJob("dropped");
            LogQueueFull(logger, vin.Masked, settings.QueueCapacity);
            return false;
        }

        if (!_pending.TryAdd(vin, 0))
        {
            return false;
        }

        metrics.RefreshJob("enqueued");
        Schedule(new RefreshJob(vin, 1));
        return true;
    }

    /// <summary>Jobs whose delay has passed, in the order they became due.</summary>
    public IAsyncEnumerable<RefreshJob> ReadAllAsync(CancellationToken cancellationToken) => _due.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Schedules the next attempt of a job that is still partial; the VIN stays queued.</summary>
    public void Retry(RefreshJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        Schedule(job with { Attempt = job.Attempt + 1 });
    }

    /// <summary>Ends a job, so a later partial search may queue the VIN again.</summary>
    public void Complete(Vin vin) => _pending.TryRemove(vin, out _);

    public void Dispose()
    {
        _stopping.Cancel();
        _due.Writer.TryComplete();
        _stopping.Dispose();
    }

    private void Schedule(RefreshJob job) => _ = ReleaseWhenDueAsync(job, options.Value.DelayBefore(job.Attempt), _stopping.Token);

    private async Task ReleaseWhenDueAsync(RefreshJob job, TimeSpan delay, CancellationToken stopping)
    {
        try
        {
            await Task.Delay(delay, timeProvider, stopping);
            _due.Writer.TryWrite(job);
        }
        catch (OperationCanceledException)
        {
            // shutting down; in-memory jobs are dropped by design
        }
    }

    [LoggerMessage(EventId = 1400, Level = LogLevel.Warning, Message = "refresh.queue_full vin={VinMasked} capacity={Capacity}")]
    private static partial void LogQueueFull(ILogger logger, string vinMasked, int capacity);
}
