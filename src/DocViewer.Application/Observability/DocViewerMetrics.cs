using System.Diagnostics.Metrics;
using System.Text.Json;
using DocViewer.Application.Sources;
using DocViewer.Domain;

namespace DocViewer.Application.Observability;

/// <summary>
/// Custom instruments on <c>Meter("DocViewer")</c> (DESIGN.md §12.2). Prometheus names are the exported
/// forms, e.g. <c>docviewer.source.requests</c> → <c>docviewer_source_requests_total</c>.
/// Tags are low-cardinality enums only: never a VIN (invariant 7, D-011).
/// </summary>
public sealed class DocViewerMetrics
{
    public const string MeterName = "DocViewer";

    private readonly Counter<long> _sourceRequests;
    private readonly Histogram<double> _sourceDuration;
    private readonly Counter<long> _cacheRequests;
    private readonly Counter<long> _partialResponses;
    private readonly Counter<long> _staleServed;
    private readonly Counter<long> _recordsDropped;

    public DocViewerMetrics(IMeterFactory meterFactory, ISourceHealth health)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(health);
        var meter = meterFactory.Create(MeterName);

        _sourceRequests = meter.CreateCounter<long>(
            "docviewer.source.requests", "{request}", "Calls to each source system, by outcome.");
        _sourceDuration = meter.CreateHistogram(
            "docviewer.source.duration", "s", "Time per source call, retry included.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 1.5, 2, 2.5, 5] });
        _cacheRequests = meter.CreateCounter<long>(
            "docviewer.cache.requests", "{request}", "Response-cache lookups: hit, miss, bypass or error.");
        _partialResponses = meter.CreateCounter<long>(
            "docviewer.partial.responses", "{response}", "Responses missing live data, once per failed source.");
        _staleServed = meter.CreateCounter<long>(
            "docviewer.stale.served", "{response}", "Responses that used a last-known-good snapshot for a source.");
        _recordsDropped = meter.CreateCounter<long>(
            "docviewer.adapter.records_dropped", "{record}", "Source records skipped because they could not be mapped.");

        meter.CreateObservableGauge(
            "docviewer.circuit.state",
            () => health.GetHealth().Select(h => new Measurement<int>(CircuitCode(h.Circuit), Tag("source", h.Source))),
            unit: "{state}",
            description: "Circuit breaker per source: 0 closed, 1 half-open, 2 open.");
    }

    public void SourceCalled(SourceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var source = Tag("source", result.Source);
        _sourceRequests.Add(1, source, new("outcome", Wire(result.Status)));
        _sourceDuration.Record(result.Latency.TotalSeconds, source);
    }

    public void CacheLookup(string result) => _cacheRequests.Add(1, new KeyValuePair<string, object?>("result", result));

    public void PartialResponse(SourceSystem failedSource) => _partialResponses.Add(1, Tag("failed_source", failedSource));

    public void StaleServed(SourceSystem source) => _staleServed.Add(1, Tag("source", source));

    public void RecordDropped(SourceSystem source, string reason) =>
        _recordsDropped.Add(1, Tag("source", source), new("reason", reason));

    private static KeyValuePair<string, object?> Tag(string name, SourceSystem source) => new(name, Wire(source));

    private static string Wire<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    // Isolated (manually held open) reports as open: traffic is blocked either way.
    private static int CircuitCode(CircuitState state) => state switch
    {
        CircuitState.Closed => 0,
        CircuitState.HalfOpen => 1,
        _ => 2,
    };
}
