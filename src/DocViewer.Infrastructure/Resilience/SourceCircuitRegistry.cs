using DocViewer.Application.Sources;
using DocViewer.Domain;
using Polly.CircuitBreaker;
using PollyCircuitState = Polly.CircuitBreaker.CircuitState;

namespace DocViewer.Infrastructure.Resilience;

/// <summary>
/// Holds one Polly <see cref="CircuitBreakerStateProvider"/> per source so circuit state can be
/// observed outside the HTTP pipeline (health endpoint, metrics). Breaker state is per process (D-014).
/// </summary>
public sealed class SourceCircuitRegistry : ISourceHealth
{
    private readonly Dictionary<SourceSystem, CircuitBreakerStateProvider> _providers =
        Enum.GetValues<SourceSystem>().ToDictionary(s => s, _ => new CircuitBreakerStateProvider());

    public CircuitBreakerStateProvider For(SourceSystem source) => _providers[source];

    public IReadOnlyList<SourceHealth> GetHealth() =>
        _providers.Select(p => new SourceHealth(p.Key, Map(p.Value.CircuitState))).ToList();

    private static Application.Sources.CircuitState Map(PollyCircuitState state) => state switch
    {
        PollyCircuitState.Open => Application.Sources.CircuitState.Open,
        PollyCircuitState.HalfOpen => Application.Sources.CircuitState.HalfOpen,
        PollyCircuitState.Isolated => Application.Sources.CircuitState.Isolated,
        _ => Application.Sources.CircuitState.Closed,
    };
}
