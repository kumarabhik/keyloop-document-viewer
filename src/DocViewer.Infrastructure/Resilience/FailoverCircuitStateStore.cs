using DocViewer.Domain;
using DocViewer.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocViewer.Infrastructure.Resilience;

/// <summary>
/// Uses the shared (Redis) store while it answers within its budget; otherwise falls back to this process's own state
/// for a cool-down, then tries Redis again. A Redis outage therefore costs one budget, not one per source call, and the
/// breaker keeps working per replica as before D-032 (invariant 10). Every call stays inside the source's 2 s total
/// timeout because the breaker runs inside it (D-005).
/// </summary>
internal sealed partial class FailoverCircuitStateStore(
    ICircuitStateStore shared,
    InMemoryCircuitStateStore local,
    IOptions<ResilienceOptions> options,
    TimeProvider timeProvider,
    ILogger<FailoverCircuitStateStore> logger) : ICircuitStateStore
{
    private long _sharedDownUntilTicks;

    /// <summary>True while calls go to the local fallback.</summary>
    public bool UsingFallback => timeProvider.GetUtcNow().UtcTicks < Interlocked.Read(ref _sharedDownUntilTicks);

    public Task<DateTimeOffset?> GetOpenUntilAsync(SourceSystem source) =>
        RunAsync(s => s.GetOpenUntilAsync(source), "get");

    public Task<WindowCounts> RecordAsync(SourceSystem source, bool failed, DateTimeOffset now, CircuitWindow window) =>
        RunAsync(s => s.RecordAsync(source, failed, now, window), "record");

    public Task OpenAsync(SourceSystem source, DateTimeOffset until, DateTimeOffset now) =>
        RunAsync(async s => { await s.OpenAsync(source, until, now); return true; }, "open");

    public Task CloseAsync(SourceSystem source) =>
        RunAsync(async s => { await s.CloseAsync(source); return true; }, "close");

    public Task<bool> TryAcquireProbeAsync(SourceSystem source, TimeSpan lease, DateTimeOffset now) =>
        RunAsync(s => s.TryAcquireProbeAsync(source, lease, now), "probe");

    private async Task<T> RunAsync<T>(Func<ICircuitStateStore, Task<T>> operation, string name)
    {
        if (UsingFallback)
        {
            return await operation(local);
        }

        var settings = options.Value;
        try
        {
            // Off the caller's thread: a first Redis call can connect synchronously before returning its task (E-019).
            return await Task.Run(() => operation(shared)).WaitAsync(settings.SharedStateTimeout, timeProvider);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _sharedDownUntilTicks, (timeProvider.GetUtcNow() + settings.SharedStateRetryAfter).UtcTicks);
            LogSharedStateUnavailable(logger, name, settings.SharedStateRetryAfterSeconds, ex);
            return await operation(local);
        }
    }

    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning,
        Message = "breaker.shared_state_unavailable operation={Operation}; using local breaker state for {RetryAfterSeconds} s")]
    private static partial void LogSharedStateUnavailable(ILogger logger, string operation, double retryAfterSeconds, Exception exception);
}
