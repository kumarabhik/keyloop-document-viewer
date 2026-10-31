using DocViewer.Domain;

namespace DocViewer.Infrastructure.Resilience;

/// <summary>
/// Where the circuit breaker keeps its state: the open-until time, the failure window and the half-open probe lease.
/// In memory for one process, Redis when replicas must share it (D-032). The decisions live in
/// <see cref="SharedCircuitBreakerStrategy"/>; a store only stores.
/// </summary>
internal interface ICircuitStateStore
{
    /// <summary>When the circuit was last opened until, or <c>null</c> when it is closed.</summary>
    Task<DateTimeOffset?> GetOpenUntilAsync(SourceSystem source);

    /// <summary>Adds one outcome to the bucket for <paramref name="now"/> and returns the totals of the current window.</summary>
    Task<WindowCounts> RecordAsync(SourceSystem source, bool failed, DateTimeOffset now, CircuitWindow window);

    Task OpenAsync(SourceSystem source, DateTimeOffset until, DateTimeOffset now);

    /// <summary>Closes the circuit and forgets the window and any probe lease.</summary>
    Task CloseAsync(SourceSystem source);

    /// <summary>Takes the single half-open probe slot for <paramref name="lease"/>; <c>false</c> when someone holds it.</summary>
    Task<bool> TryAcquireProbeAsync(SourceSystem source, TimeSpan lease, DateTimeOffset now);
}
