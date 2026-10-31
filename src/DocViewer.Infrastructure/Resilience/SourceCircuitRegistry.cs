using System.Collections.Concurrent;
using DocViewer.Application.Sources;
using DocViewer.Domain;

namespace DocViewer.Infrastructure.Resilience;

/// <summary>
/// Circuit state per source for the health endpoint and the circuit gauge. <see cref="GetHealth"/> is what this process
/// last saw (the gauge is synchronous); <see cref="ReadHealthAsync"/> asks the store, so with Redis it also shows a
/// circuit another replica opened (D-032).
/// </summary>
internal sealed class SourceCircuitRegistry(ICircuitStateStore store, TimeProvider timeProvider) : ISourceHealth
{
    private readonly ConcurrentDictionary<SourceSystem, CircuitState> _observed =
        new(Enum.GetValues<SourceSystem>().ToDictionary(s => s, _ => CircuitState.Closed));

    public void Observe(SourceSystem source, CircuitState state) => _observed[source] = state;

    public IReadOnlyList<SourceHealth> GetHealth() =>
        [.. _observed.OrderBy(p => p.Key).Select(p => new SourceHealth(p.Key, p.Value))];

    public async Task<IReadOnlyList<SourceHealth>> ReadHealthAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var source in Enum.GetValues<SourceSystem>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var openUntil = await store.GetOpenUntilAsync(source);
            Observe(source, openUntil switch
            {
                null => CircuitState.Closed,
                { } until when now < until => CircuitState.Open,
                _ => CircuitState.HalfOpen,
            });
        }

        return GetHealth();
    }
}
