using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;

namespace DocViewer.Api.Tests;

/// <summary>D-033 through the real host: the hosted worker refreshes a partial result, and the next search is a complete hit.</summary>
public sealed class BackgroundRefreshApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task PartialResult_IsRefreshedInTheBackground_ThenServedCompleteFromTheCache()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Refresh:FirstDelaySeconds", "1"));
        using var client = factory.CreateClient();
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 500, "{}");

        var first = await GetAsync(client);
        first["partial"]!.GetValue<bool>().ShouldBeTrue();

        _factory.Service.Reset();
        _factory.ServiceReturns(Payloads.Vin, 200, Payloads.Service);

        // No searches while waiting: a search after the heal would itself be complete and fill the cache, so the test
        // would pass without the worker. Wait until the worker has called Service (real time, an integration test).
        var waited = Stopwatch.StartNew();
        while (_factory.Service.LogEntries.Count == 0 && waited.Elapsed < TimeSpan.FromSeconds(3))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        _factory.Service.LogEntries.Count.ShouldBe(1);
        await Task.Delay(TimeSpan.FromMilliseconds(200)); // the cache write follows the fan-out
        var next = await GetAsync(client);

        next["cache"]!.GetValue<string>().ShouldBe("HIT");
        next["partial"]!.GetValue<bool>().ShouldBeFalse();
    }

    private static async Task<JsonNode> GetAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/vehicles/{Payloads.Vin}/documents", UriKind.Relative));
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
}
