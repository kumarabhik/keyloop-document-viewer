using System.Diagnostics;

namespace DocViewer.Api.Tests;

public sealed class TelemetryTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public TelemetryTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private Task<HttpResponseMessage> SearchAsync(string query = "") =>
        _client.GetAsync(new Uri($"/api/v1/vehicles/{Payloads.Vin}/documents{query}", UriKind.Relative));

    private static ActivityTraceId TraceIdOf(HttpResponseMessage response) =>
        ActivityTraceId.CreateFromString(response.Headers.GetValues("X-Trace-Id").Single());

    private async Task<string> ScrapeAsync() =>
        await _client.GetStringAsync(new Uri("/metrics", UriKind.Relative));

    [Fact]
    [Trait("Invariant", "7")]
    public async Task Metrics_AreExposedInPrometheusFormat_WithoutVinLabels()
    {
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 503, "{}"); // retried once, then partial

        await SearchAsync();
        await SearchAsync();
        var scrape = await ScrapeAsync();

        scrape.ShouldContain("docviewer_source_requests_total{");
        scrape.ShouldContain("outcome=\"ok\"");
        scrape.ShouldContain("source=\"service\"");
        scrape.ShouldContain("docviewer_source_duration_seconds_bucket{");
        scrape.ShouldContain("docviewer_cache_requests_total{");
        scrape.ShouldContain("docviewer_partial_responses_total{");
        scrape.ShouldContain("failed_source=\"service\"");
        scrape.ShouldContain("docviewer_circuit_state{");
        scrape.ShouldContain("resilience_polly_strategy_events_total{"); // the retry on 503
        scrape.ShouldContain("http_server_request_duration_seconds_bucket{");
        scrape.ShouldNotContain("MA3EWB81S", Case.Insensitive);
    }

    [Fact]
    public async Task Trace_ShowsParallelSourceSpansUnderOneAggregate()
    {
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 200, Payloads.Service);

        var response = await SearchAsync();
        var traceId = ActivityTraceId.CreateFromString(response.Headers.GetValues("X-Trace-Id").Single());
        await Task.Delay(300); // background snapshot/audit spans end after the response

        var spans = _factory.Spans.ForTraces(traceId);
        var aggregate = spans.Single(s => s.OperationName == "aggregate");
        var sales = spans.Single(s => s.OperationName == "source.sales");
        var service = spans.Single(s => s.OperationName == "source.service");

        sales.ParentSpanId.ShouldBe(aggregate.SpanId);
        service.ParentSpanId.ShouldBe(aggregate.SpanId);
        // Siblings that overlap in time: the fan-out really is parallel.
        sales.StartTimeUtc.ShouldBeLessThan(service.StartTimeUtc + service.Duration);
        service.StartTimeUtc.ShouldBeLessThan(sales.StartTimeUtc + sales.Duration);
        spans.ShouldContain(s => s.OperationName == "cache.get");
        spans.ShouldContain(s => s.OperationName == "merge");
        spans.ShouldContain(s => s.Kind == ActivityKind.Client && s.ParentSpanId == sales.SpanId); // outgoing HTTP
        aggregate.GetTagItem("docviewer.outcome").ShouldBe("Complete");
    }

    [Fact]
    public async Task TraceContext_IsPropagatedToSourceSystems()
    {
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 200, Payloads.Service);

        var response = await SearchAsync();
        var traceId = response.Headers.GetValues("X-Trace-Id").Single();

        var headers = _factory.Sales.LogEntries.Single().RequestMessage?.Headers;
        headers.ShouldNotBeNull();
        var traceparent = headers["traceparent"].Single();
        traceparent.ShouldContain(traceId);
    }

    [Fact]
    [Trait("Invariant", "7")]
    public async Task ExportedSpans_NeverContainRawVin()
    {
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 500, "{}");

        var valid = await SearchAsync();
        var invalid = await _client.GetAsync(new Uri("/api/v1/vehicles/MA3EWB81S0010000O/documents", UriKind.Relative));
        await Task.Delay(300);
        var spans = _factory.Spans.ForTraces(TraceIdOf(valid), TraceIdOf(invalid));

        spans.Count.ShouldBeGreaterThan(5);
        foreach (var span in spans)
        {
            span.DisplayName.ShouldNotContain("MA3EWB81S001000", Case.Insensitive);
            foreach (var tag in span.TagObjects)
            {
                (tag.Value?.ToString() ?? "").ShouldNotContain("MA3EWB81S001000", Case.Insensitive, $"tag {tag.Key} on {span.DisplayName}");
            }
        }

        spans.ShouldContain(s => s.TagObjects.Any(t => (t.Value as string ?? "").Contains("MA3**********0001", StringComparison.Ordinal)));
    }
}
