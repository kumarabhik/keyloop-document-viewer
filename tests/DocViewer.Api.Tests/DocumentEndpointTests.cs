using System.Net;
using System.Text.Json.Nodes;
using DocViewer.Api.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DocViewer.Api.Tests;

public sealed class DocumentEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public DocumentEndpointTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private void BothSourcesHealthy()
    {
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 200, Payloads.Service);
    }

    private async Task<(HttpResponseMessage Response, JsonNode Body)> GetAsync(string path)
    {
        var response = await _client.GetAsync(new Uri(path, UriKind.Relative));
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return (response, body);
    }

    private static string DocumentsPath(string vin = Payloads.Vin, string query = "") => $"/api/v1/vehicles/{vin}/documents{query}";

    [Theory]
    [InlineData("MA3EWB81S0010000", "16 characters")]
    [InlineData("MA3EWB81S0010000O", "'O'")]
    [InlineData("BADVIN", "6 characters")]
    [Trait("Invariant", "6")]
    public async Task InvalidVin_Returns400Problem_WithoutCallingSources(string vin, string expectedDetail)
    {
        var (response, body) = await GetAsync(DocumentsPath(vin));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        body["type"]!.GetValue<string>().ShouldBe("https://docviewer.local/problems/invalid-vin");
        body["title"]!.GetValue<string>().ShouldBe("Invalid VIN");
        body["detail"]!.GetValue<string>().ShouldContain(expectedDetail);
        body["instance"]!.GetValue<string>().ShouldBe(DocumentsPath(vin));
        body["traceId"]!.GetValue<string>().ShouldBe(response.Headers.GetValues("X-Trace-Id").Single());
        _factory.Sales.LogEntries.ShouldBeEmpty();
        _factory.Service.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task BothSourcesOk_Returns200WithMergedDocumentsAndWireFormat()
    {
        BothSourcesHealthy();

        var (response, body) = await GetAsync(DocumentsPath("ma3ewb81s00100001")); // normalised server-side

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Cache").Single().ShouldBe("MISS");
        body["vin"]!.GetValue<string>().ShouldBe(Payloads.Vin);
        body["partial"]!.GetValue<bool>().ShouldBeFalse();
        body["cache"]!.GetValue<string>().ShouldBe("MISS");
        body["traceId"]!.GetValue<string>().ShouldMatch("^[0-9a-f]{32}$");

        var sources = body["sources"]!.AsArray();
        sources.Select(s => s!["name"]!.GetValue<string>()).ShouldBe(["SALES", "SERVICE"]);
        sources.ShouldAllBe(s => s!["status"]!.GetValue<string>() == "ok");
        sources[0]!["count"]!.GetValue<int>().ShouldBe(2);
        sources[0]!.AsObject().ContainsKey("error").ShouldBeFalse();

        var documents = body["documents"]!.AsArray();
        documents.Count.ShouldBe(4);
        documents.ShouldAllBe(d => d!["source"] != null); // invariant 2
        var puc = documents.Single(d => d!["id"]!.GetValue<string>() == "SERVICE:SV-PUC-1")!;
        puc["type"]!.GetValue<string>().ShouldBe("PUC");
        puc["source"]!.GetValue<string>().ShouldBe("SERVICE");
        puc["status"]!.GetValue<string>().ShouldBe("VALID");
        puc["date"]!.GetValue<string>().ShouldBe("2026-03-01");
        puc["expiryDate"]!.GetValue<string>().ShouldBe("2099-08-31");

        var salesInvoice = documents.Single(d => d!["id"]!.GetValue<string>() == "SALES:INV-2024-0311")!;
        salesInvoice["status"]!.GetValue<string>().ShouldBe("NOT_APPLICABLE");
        salesInvoice["flags"]!.AsArray().Select(f => f!.GetValue<string>()).ShouldBe(["POSSIBLE_DUPLICATE"]);
        salesInvoice["metadata"]!["duplicateOf"]!.GetValue<string>().ShouldBe("SERVICE:SV-INV-BLR/24/0311");

        var summary = body["summary"]!;
        summary["total"]!.GetValue<int>().ShouldBe(4);
        summary["expired"]!.GetValue<int>().ShouldBe(1);
        summary["duplicates"]!.GetValue<int>().ShouldBe(2);
    }

    [Fact]
    public async Task SecondCall_IsServedFromCache()
    {
        BothSourcesHealthy();

        await GetAsync(DocumentsPath());
        var (response, body) = await GetAsync(DocumentsPath());

        response.Headers.GetValues("X-Cache").Single().ShouldBe("HIT");
        body["cache"]!.GetValue<string>().ShouldBe("HIT");
        _factory.Sales.LogEntries.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Refresh_BypassesCache()
    {
        BothSourcesHealthy();

        await GetAsync(DocumentsPath());
        var (response, _) = await GetAsync(DocumentsPath(query: "?refresh=true"));

        response.Headers.GetValues("X-Cache").Single().ShouldBe("BYPASS");
        _factory.Sales.LogEntries.Count.ShouldBe(2);
    }

    [Fact]
    [Trait("Invariant", "1")]
    [Trait("Invariant", "3")]
    public async Task ServiceFails_Returns200PartialWithSalesDocuments()
    {
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 500, "{}");

        var (response, body) = await GetAsync(DocumentsPath());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body["partial"]!.GetValue<bool>().ShouldBeTrue();
        var service = body["sources"]!.AsArray().Single(s => s!["name"]!.GetValue<string>() == "SERVICE")!;
        service["status"]!.GetValue<string>().ShouldBe("error");
        service["error"]!.GetValue<string>().ShouldBe("http_500");
        body["documents"]!.AsArray().ShouldAllBe(d => d!["source"]!.GetValue<string>() == "SALES");
    }

    [Fact]
    [Trait("Invariant", "4")]
    public async Task PartialResponse_IsNotCached()
    {
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 500, "{}");

        await GetAsync(DocumentsPath());
        var (response, body) = await GetAsync(DocumentsPath());

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        response.Headers.GetValues("X-Cache").Single().ShouldBe("MISS");
        _factory.Sales.LogEntries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task BothSourcesFail_Returns503ProblemWithRetryAfter()
    {
        _factory.SalesReturns(Payloads.Vin, 500, "{}");
        _factory.ServiceReturns(Payloads.Vin, 500, "{}");

        var (response, body) = await GetAsync(DocumentsPath());

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(5));
        body["type"]!.GetValue<string>().ShouldBe("https://docviewer.local/problems/sources-unavailable");
        body["traceId"].ShouldNotBeNull();
        var sources = body["sources"]!.AsArray();
        sources.Select(s => s!["name"]!.GetValue<string>()).ShouldBe(["SALES", "SERVICE"]);
        sources.ShouldAllBe(s => s!["status"]!.GetValue<string>() == "error");
    }

    [Fact]
    public async Task SourceFailsAfterEarlierSuccess_ServesLabelledStaleSnapshot()
    {
        BothSourcesHealthy();
        await GetAsync(DocumentsPath());
        _factory.Service.Reset();
        _factory.ServiceReturns(Payloads.Vin, 500, "{}");

        // Snapshots are written in the background after the first response; allow it a moment.
        JsonNode? service = null;
        for (var attempt = 0; attempt < 50 && service?["status"]?.GetValue<string>() != "stale"; attempt++)
        {
            await Task.Delay(100);
            var (_, body) = await GetAsync(DocumentsPath(query: "?refresh=true"));
            service = body["sources"]!.AsArray().Single(s => s!["name"]!.GetValue<string>() == "SERVICE");
        }

        service!["status"]!.GetValue<string>().ShouldBe("stale");
        service["error"]!.GetValue<string>().ShouldBe("http_500");
        service["asOf"].ShouldNotBeNull();
        service["count"]!.GetValue<int>().ShouldBe(2);
    }

    [Fact]
    public async Task UnknownVehicleInBothSystems_Returns200WithEmptyList()
    {
        _factory.SalesReturns(Payloads.Vin, 404, Payloads.SalesNotFound);
        _factory.ServiceReturns(Payloads.Vin, 200, Payloads.ServiceEmpty);

        var (response, body) = await GetAsync(DocumentsPath());

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToJsonString()); // D-004
        body["partial"]!.GetValue<bool>().ShouldBeFalse();
        body["documents"]!.AsArray().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("?source=SERVICE", new[] { "SERVICE:SV-PUC-1", "SERVICE:SV-INV-BLR/24/0311" })]
    [InlineData("?type=insurance", new[] { "SALES:INS-1" })]
    [InlineData("?status=EXPIRED", new[] { "SALES:INS-1" })]
    [InlineData("?type=PUC,INSURANCE", new[] { "SERVICE:SV-PUC-1", "SALES:INS-1" })]
    [InlineData("?source=SALES&type=INVOICE", new[] { "SALES:INV-2024-0311" })]
    public async Task Filters_ApplyAfterMerge(string query, string[] expectedIds)
    {
        BothSourcesHealthy();

        var (_, body) = await GetAsync(DocumentsPath(query: query));

        body["documents"]!.AsArray().Select(d => d!["id"]!.GetValue<string>()).ShouldBe(expectedIds);
        body["summary"]!["total"]!.GetValue<int>().ShouldBe(expectedIds.Length);
    }

    [Theory]
    [InlineData("?source=DMS", "Unknown source 'DMS'")]
    [InlineData("?status=1", "Unknown status '1'")]
    public async Task InvalidFilter_Returns400Problem(string query, string expectedDetail)
    {
        var (response, body) = await GetAsync(DocumentsPath(query: query));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        body["type"]!.GetValue<string>().ShouldBe("https://docviewer.local/problems/invalid-filter");
        body["detail"]!.GetValue<string>().ShouldStartWith(expectedDetail);
    }

    [Fact]
    public async Task SourceHealth_ReportsCircuitAndLastOutcome()
    {
        var (_, before) = await GetAsync("/api/v1/sources/health");
        before.AsArray().Select(s => s!["name"]!.GetValue<string>()).ShouldBe(["SALES", "SERVICE"]);
        before.AsArray().ShouldAllBe(s => s!["circuit"]!.GetValue<string>() == "closed" && s["lastStatus"] == null);

        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 500, "{}");
        await GetAsync(DocumentsPath());
        var (_, after) = await GetAsync("/api/v1/sources/health");

        after[0]!["lastStatus"]!.GetValue<string>().ShouldBe("ok");
        after[1]!["lastStatus"]!.GetValue<string>().ShouldBe("error");
        after[1]!["lastLatencyMs"]!.GetValue<long>().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    public async Task HealthEndpoints_Return200(string path)
    {
        var response = await _client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    [Trait("Invariant", "10")]
    public async Task DatabaseDown_ReadyzIsDegraded_AndSearchStillSucceeds()
    {
        BothSourcesHealthy();
        // Nothing listens on port 1, so every database call is refused at once (D-024).
        using var factory = _factory.WithWebHostBuilder(b => b
            .UseSetting("Persistence:Provider", "Postgres")
            .UseSetting("ConnectionStrings:DocViewer", "Host=127.0.0.1;Port=1;Timeout=1;Username=x;Password=x;Database=x"));
        using var client = factory.CreateClient();

        var ready = await client.GetAsync(new Uri("/readyz", UriKind.Relative));
        var search = await client.GetAsync(new Uri(DocumentsPath(), UriKind.Relative));

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync()).ShouldBe("Degraded");
        search.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public void DatabaseCheck_TimesOutFastAsDegraded_SoTheContainerProbeNeverWaitsOnTheDatabase()
    {
        var registration = _factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(r => r.Name == DatabaseHealthCheck.Name);

        registration.FailureStatus.ShouldBe(HealthStatus.Degraded);
        registration.Timeout.ShouldBe(TimeSpan.FromSeconds(1));
        registration.Timeout.ShouldBeLessThan(TimeSpan.FromSeconds(3)); // ContainerHealthCheck's probe timeout (D-023)
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemWithTraceId()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/nope", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.Contains("X-Trace-Id").ShouldBeTrue();
    }

    [Fact]
    public async Task OpenApiDocument_IsServed()
    {
        var (response, body) = await GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body["paths"]!["/api/v1/vehicles/{vin}/documents"].ShouldNotBeNull();
    }
}
