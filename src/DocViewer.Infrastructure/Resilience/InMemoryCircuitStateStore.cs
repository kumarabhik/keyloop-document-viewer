using DocViewer.Domain;

namespace DocViewer.Infrastructure.Resilience;

/// <summary>Breaker state for one process: the default without Redis, and the fallback while Redis is unreachable.</summary>
internal sealed class InMemoryCircuitStateStore : ICircuitStateStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<SourceSystem, State> _states = [];

    public Task<DateTimeOffset?> GetOpenUntilAsync(SourceSystem source)
    {
        lock (_gate)
        {
            return Task.FromResult(For(source).OpenUntil);
        }
    }

    public Task<WindowCounts> RecordAsync(SourceSystem source, bool failed, DateTimeOffset now, CircuitWindow window)
    {
        lock (_gate)
        {
            var buckets = For(source).Buckets;
            var bucket = window.BucketOf(now);
            buckets.TryGetValue(bucket, out var counts);
            buckets[bucket] = failed ? counts with { Failures = counts.Failures + 1 } : counts with { Successes = counts.Successes + 1 };

            var oldest = window.OldestBucket(now);
            foreach (var stale in buckets.Keys.Where(b => b < oldest).ToList())
            {
                buckets.Remove(stale);
            }

            return Task.FromResult(new WindowCounts(buckets.Values.Sum(c => c.Successes), buckets.Values.Sum(c => c.Failures)));
        }
    }

    public Task OpenAsync(SourceSystem source, DateTimeOffset until, DateTimeOffset now)
    {
        lock (_gate)
        {
            For(source).OpenUntil = until;
        }

        return Task.CompletedTask;
    }

    public Task CloseAsync(SourceSystem source)
    {
        lock (_gate)
        {
            _states.Remove(source);
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryAcquireProbeAsync(SourceSystem source, TimeSpan lease, DateTimeOffset now)
    {
        lock (_gate)
        {
            var state = For(source);
            if (state.ProbeUntil > now)
            {
                return Task.FromResult(false);
            }

            state.ProbeUntil = now + lease;
            return Task.FromResult(true);
        }
    }

    private State For(SourceSystem source)
    {
        if (!_states.TryGetValue(source, out var state))
        {
            state = new State();
            _states[source] = state;
        }

        return state;
    }

    private sealed class State
    {
        public DateTimeOffset? OpenUntil { get; set; }

        public DateTimeOffset ProbeUntil { get; set; }

        public Dictionary<long, WindowCounts> Buckets { get; } = [];
    }
}
