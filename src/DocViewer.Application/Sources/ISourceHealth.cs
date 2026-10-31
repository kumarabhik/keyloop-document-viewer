namespace DocViewer.Application.Sources;

/// <summary>Read-only view of each source's circuit breaker, for <c>/api/v1/sources/health</c> and metrics.</summary>
public interface ISourceHealth
{
    /// <summary>The state this process last observed; cheap and synchronous, for gauges.</summary>
    IReadOnlyList<SourceHealth> GetHealth();

    /// <summary>The current state from the breaker's store, which may be shared by every replica (D-032).</summary>
    Task<IReadOnlyList<SourceHealth>> ReadHealthAsync(CancellationToken cancellationToken);
}
