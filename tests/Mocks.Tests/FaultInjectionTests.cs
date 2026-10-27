using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SalesSystem.Mock;

namespace Mocks.Tests;

/// <summary>Fault switch behaviour, exercised through the Sales mock (both mocks share Mocks.Common).</summary>
public sealed class FaultInjectionTests : IDisposable
{
    private static readonly Uri DocumentsUri = new("/sales/v1/vehicles/MA3EWB81S00100001/documents", UriKind.Relative);
    private static readonly Uri FaultUri = new("/__admin/fault", UriKind.Relative);

    private readonly WebApplicationFactory<SalesMockMarker> _factory = new();
    private readonly HttpClient _client;

    public FaultInjectionTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task SetFaultAsync(object settings)
    {
        var response = await _client.PostAsJsonAsync(FaultUri, settings);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Default_IsHealthy()
    {
        var current = await _client.GetFromJsonAsync<JsonElement>(FaultUri);

        current.GetProperty("mode").GetString().ShouldBe("none");
        (await _client.GetAsync(DocumentsUri)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ErrorMode_Returns500()
    {
        await SetFaultAsync(new { mode = "error" });

        (await _client.GetAsync(DocumentsUri)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task MalformedMode_Returns200WithUnparseableJson()
    {
        await SetFaultAsync(new { mode = "malformed" });

        var response = await _client.GetAsync(DocumentsUri);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        Should.Throw<JsonException>(() => JsonDocument.Parse(body));
    }

    [Theory]
    [InlineData(1.0, HttpStatusCode.ServiceUnavailable)]
    [InlineData(0.0, HttpStatusCode.OK)]
    public async Task FlakyMode_FailsTheConfiguredShare(double rate, HttpStatusCode expected)
    {
        await SetFaultAsync(new { mode = "flaky", rate });

        for (var i = 0; i < 5; i++)
        {
            (await _client.GetAsync(DocumentsUri)).StatusCode.ShouldBe(expected);
        }
    }

    [Theory]
    [InlineData("slow")]
    [InlineData("timeout")]
    public async Task DelayModes_DelayThenRespondNormally(string mode)
    {
        await SetFaultAsync(new { mode, delayMs = 250 });
        var stopwatch = Stopwatch.StartNew();

        var response = await _client.GetAsync(DocumentsUri);

        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(240));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminAndHealthEndpoints_AreNeverFaulted()
    {
        await SetFaultAsync(new { mode = "error" });

        (await _client.GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.GetAsync(FaultUri)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResettingToNone_RestoresNormalBehaviour()
    {
        await SetFaultAsync(new { mode = "error" });
        await SetFaultAsync(new { mode = "none" });

        (await _client.GetAsync(DocumentsUri)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("""{"mode":"flaky","rate":1.5}""")]
    [InlineData("""{"mode":"slow","delayMs":-1}""")]
    [InlineData("""{"mode":"explode"}""")]
    public async Task InvalidSettings_AreRejectedWith400(string json)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync(FaultUri, content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
