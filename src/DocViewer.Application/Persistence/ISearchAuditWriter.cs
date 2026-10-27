namespace DocViewer.Application.Persistence;

/// <summary>Records one row per search. Best effort: failures are logged, never surfaced (invariant 10).</summary>
public interface ISearchAuditWriter
{
    Task WriteAsync(SearchAuditEntry entry, CancellationToken cancellationToken);
}
