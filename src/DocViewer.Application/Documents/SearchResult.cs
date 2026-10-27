using DocViewer.Application.Persistence;
using DocViewer.Application.Sources;

namespace DocViewer.Application.Documents;

public sealed record SearchResult(AggregatedDocuments Documents, CacheStatus Cache)
{
    /// <summary>
    /// <see cref="SearchOutcome.Failed"/> only when no source answered and no snapshot could stand in
    /// (DESIGN.md §6.3: the one case that becomes a 503).
    /// </summary>
    public SearchOutcome Outcome =>
        !Documents.Partial ? SearchOutcome.Complete
        : Documents.Sources.Any(s => s.Status is SourceStatus.Ok or SourceStatus.Stale) ? SearchOutcome.Partial
        : SearchOutcome.Failed;
}
