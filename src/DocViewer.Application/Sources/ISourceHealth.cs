namespace DocViewer.Application.Sources;

/// <summary>Read-only view of each source's circuit breaker, for <c>/api/v1/sources/health</c> and metrics.</summary>
public interface ISourceHealth
{
    IReadOnlyList<SourceHealth> GetHealth();
}
