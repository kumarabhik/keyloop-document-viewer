using DocViewer.Domain;

namespace DocViewer.Application.Sources;

/// <summary>
/// Port to one external document system. Implementations translate the system's own
/// payload into canonical <see cref="Document"/>s.
/// </summary>
public interface ISourceAdapter
{
    SourceSystem Source { get; }

    /// <summary>
    /// Must not throw for source failures (timeouts, HTTP errors, bad payloads): return a failed
    /// <see cref="SourceResult"/> instead. Only cancellation of <paramref name="cancellationToken"/> may throw.
    /// </summary>
    Task<SourceResult> GetDocumentsAsync(Vin vin, CancellationToken cancellationToken);
}
