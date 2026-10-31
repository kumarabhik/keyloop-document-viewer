using System.Diagnostics;
using System.Text.Json.Nodes;
using DocViewer.Application.Documents;
using DocViewer.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace DocViewer.Api.Tests;

/// <summary>D-033 through the real host: the hosted worker refreshes a partial result, and the next search is a complete hit.</summary>
public sealed class BackgroundRefreshApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task PartialResult_IsRefreshedInTheBackground_ThenServedCompleteFromTheCache()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Refresh:FirstDelaySeconds", "0.5"));
        using var client = factory.CreateClient();
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        // Service fails the user's search and answers every later call: no "heal" step that could race the worker.
        var records = Request.Create().WithPath("/service/api/records").WithParam("vin", Payloads.Vin).UsingGet();
        _factory.Service.Given(records).InScenario("recovery").WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(500));
        _factory.Service.Given(records).InScenario("recovery").WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(Payloads.Service));

        var first = await GetAsync(client);
        first["partial"]!.GetValue<bool>().ShouldBeTrue();

        // Wait for the worker's cache write by reading the cache itself: a search would fill it on its own and prove nothing.
        var cache = factory.Services.GetRequiredService<IDocumentCache>();
        var waited = Stopwatch.StartNew();
        while (await cache.GetAsync(Vin.Parse(Payloads.Vin), CancellationToken.None) is null && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50)); // real time on purpose: the worker runs on the host's clock
        }

        var next = await GetAsync(client);
        next["cache"]!.GetValue<string>().ShouldBe("HIT");
        next["partial"]!.GetValue<bool>().ShouldBeFalse();
        _factory.Service.LogEntries.Count.ShouldBe(2); // the user's search and one background refresh
    }

    private static async Task<JsonNode> GetAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/vehicles/{Payloads.Vin}/documents", UriKind.Relative));
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
}
