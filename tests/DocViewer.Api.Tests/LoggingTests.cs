using System.Diagnostics;
using Serilog.Events;

namespace DocViewer.Api.Tests;

public sealed class LoggingTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public LoggingTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private Task<HttpResponseMessage> GetAsync(string path) => _client.GetAsync(new Uri(path, UriKind.Relative));

    /// <summary>Background writes log after the response; give them a moment so their events are checked too.</summary>
    private async Task<IReadOnlyList<LogEvent>> SettledEventsAsync()
    {
        await Task.Delay(300);
        return [.. _factory.Logs.Events];
    }

    [Fact]
    [Trait("Invariant", "7")]
    public async Task RawVin_NeverAppearsInAnyLogEvent()
    {
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 500, "{}"); // failure path logs more

        await GetAsync($"/api/v1/vehicles/{Payloads.Vin}/documents");
        await GetAsync($"/api/v1/vehicles/{Payloads.Vin.ToLowerInvariant()}/documents?refresh=true");
        await GetAsync("/api/v1/vehicles/MA3EWB81S0010000O/documents"); // invalid but VIN-like
        var events = await SettledEventsAsync();

        events.ShouldNotBeEmpty();
        foreach (var json in events.Select(CollectingSink.Render))
        {
            json.ShouldNotContain("MA3EWB81S001000", Case.Insensitive);
        }

        events.Select(CollectingSink.Render).ShouldContain(j => j.Contains("MA3**********0001", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Invariant", "7")]
    public async Task RawVin_NeverAppearsInLogsWrittenInsideTheSourcePipeline()
    {
        // IHttpClientFactory opens a log scope "HTTP GET {Uri}" around each source call; anything logged inside the
        // pipeline inherited the source URL, VIN included (E-025): Polly's retry warning, and the shared-breaker
        // fallback warning (D-032) seen on Kubernetes. A 503 makes Polly retry; an unreachable Redis makes the breaker
        // log its fallback.
        using var factory = _factory.WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,connectTimeout=100,abortConnect=false")
            .UseSetting("Resilience:SharedStateTimeoutMilliseconds", "5"));
        using var client = factory.CreateClient();
        _factory.SalesReturns(Payloads.Vin, 503, "{}");
        _factory.ServiceReturns(Payloads.Vin, 200, Payloads.Service);

        (await client.GetAsync(new Uri($"/api/v1/vehicles/{Payloads.Vin}/documents?refresh=true", UriKind.Relative))).EnsureSuccessStatusCode();
        var events = await SettledEventsAsync();

        var rendered = events.Select(CollectingSink.Render).ToList();
        rendered.ShouldContain(j => j.Contains("breaker.shared_state_unavailable", StringComparison.Ordinal)); // the path ran
        rendered.Where(j => j.Contains("MA3EWB81S001000", StringComparison.OrdinalIgnoreCase))
            .Select(j => j[..Math.Min(160, j.Length)]).ShouldBeEmpty(); // lists the leaking events if any
    }

    [Fact]
    public async Task AccessLog_UsesRouteTemplateAndRequestTraceId()
    {
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 200, Payloads.Service);

        var response = await GetAsync($"/api/v1/vehicles/{Payloads.Vin}/documents");
        var traceId = response.Headers.GetValues("X-Trace-Id").Single();
        var events = await SettledEventsAsync();

        var access = events.Single(e => e.MessageTemplate.Text.StartsWith("http.request", StringComparison.Ordinal));
        access.Properties["Route"].ToString().ShouldBe("\"/api/v1/vehicles/{vin}/documents\"");
        access.Properties["StatusCode"].ToString().ShouldBe("200");
        access.Properties.ShouldNotContainKey("RequestPath");
        access.TraceId.ShouldBe(ActivityTraceId.CreateFromString(traceId));
    }

    [Fact]
    public async Task EventsDuringARequest_CarryItsTraceId()
    {
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 500, "{}");

        var response = await GetAsync($"/api/v1/vehicles/{Payloads.Vin}/documents");
        var traceId = ActivityTraceId.CreateFromString(response.Headers.GetValues("X-Trace-Id").Single());
        var events = await SettledEventsAsync();

        string[] requestScoped = ["search.completed", "source.fetched", "source.failed", "http.request"];
        var scoped = events.Where(e => requestScoped.Any(p => e.MessageTemplate.Text.StartsWith(p, StringComparison.Ordinal))).ToList();
        scoped.Count.ShouldBe(4);
        scoped.ShouldAllBe(e => e.TraceId == traceId);
    }
}
