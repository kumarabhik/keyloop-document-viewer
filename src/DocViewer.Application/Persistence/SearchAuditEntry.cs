using DocViewer.Application.Sources;
using DocViewer.Domain;

namespace DocViewer.Application.Persistence;

/// <param name="VinInput">The VIN as searched. The writer stores only a salted hash of it (D-011).</param>
public sealed record SearchAuditEntry(
    string TraceId,
    string VinInput,
    SearchOutcome Outcome,
    bool CacheHit,
    TimeSpan Latency,
    IReadOnlyDictionary<SourceSystem, SourceStatus> SourceStatuses,
    int DocumentCount);
