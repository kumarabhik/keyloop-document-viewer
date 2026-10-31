using DocViewer.Domain;

namespace DocViewer.Application.Documents;

/// <param name="Refresh">Skip the cache read (the result is still cached if complete).</param>
/// <param name="TraceId">Correlates the audit row with logs and traces.</param>
/// <param name="Scope">Dealer scope of the caller when auth is on (D-034); <c>null</c> sees everything.</param>
public sealed record SearchRequest(Vin Vin, bool Refresh, string TraceId, DealerScope? Scope = null);
