using DocViewer.Domain;

namespace DocViewer.Application.Documents;

/// <param name="Refresh">Skip the cache read (the result is still cached if complete).</param>
/// <param name="TraceId">Correlates the audit row with logs and traces.</param>
public sealed record SearchRequest(Vin Vin, bool Refresh, string TraceId);
