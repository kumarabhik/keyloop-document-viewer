using DocViewer.Domain;

namespace DocViewer.Application.Documents;

/// <summary>
/// Cache of complete aggregated results, keyed by VIN. Implementations must treat their own
/// failures as a miss / no-op: a cache outage degrades speed, never correctness (invariant 10).
/// </summary>
public interface IDocumentCache
{
    Task<AggregatedDocuments?> GetAsync(Vin vin, CancellationToken cancellationToken);

    /// <summary>Callers must only pass complete results; partial results are never cached (D-006).</summary>
    Task SetAsync(AggregatedDocuments documents, CancellationToken cancellationToken);
}
