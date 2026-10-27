using DocViewer.Domain;

namespace DocViewer.Application.Persistence;

/// <summary>
/// Last-known-good documents per (VIN, source), used as a labelled fallback when a source is down (D-003).
/// Implementations swallow their own failures: a database outage disables the fallback, nothing more.
/// </summary>
public interface ISnapshotRepository
{
    /// <summary>Returns null when there is no snapshot, it is older than the configured maximum age, or the store fails.</summary>
    Task<SourceSnapshot?> GetAsync(Vin vin, SourceSystem source, CancellationToken cancellationToken);

    Task SaveAsync(Vin vin, SourceSystem source, IReadOnlyList<Document> documents, CancellationToken cancellationToken);
}
