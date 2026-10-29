using System.Diagnostics;
using DocViewer.Application.Sources;
using DocViewer.Domain;
using DocViewer.Infrastructure.Sources.Sales;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace DocViewer.Infrastructure.Tests.Resilience;

/// <summary>
/// Exercises the real DI-built pipeline (D-014) against a real HTTP server.
/// Budgets are shortened via configuration to keep the suite fast; the shape is production's.
/// </summary>
[Collection(TimingSensitiveGroup.Name)]
public sealed class SourcePipelineTests : IDisposable
{
    private static readonly Vin Vin = Vin.Parse("MA3EWB81S00100001");
    private const string Path = "/sales/v1/vehicles/MA3EWB81S00100001/documents";
    private const string EmptyBody = """{"documents":[]}""";

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly ServiceProvider _provider;

    public SourcePipelineTests()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sources:Sales:BaseUrl"] = _server.Url,
            ["Sources:Service:BaseUrl"] = "http://localhost:1",
            // Attempt budget must comfortably exceed a cold first request (JIT + connect) — see E-001.
            ["Resilience:TotalTimeoutSeconds"] = "2.0",
            ["Resilience:AttemptTimeoutSeconds"] = "1.0",
            ["Resilience:ConnectTimeoutSeconds"] = "0.2",
            ["Resilience:RetryBaseDelayMilliseconds"] = "10",
            ["Resilience:BreakerSamplingSeconds"] = "30",
            ["Resilience:BreakerBreakSeconds"] = "0.5",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSourceAdapters();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _server.Stop();
        _server.Dispose();
    }

    private SalesAdapter Adapter => _provider.GetRequiredService<SalesAdapter>();

    private int RequestCount => _server.LogEntries.Count;

    private void Respond(int status, TimeSpan? delay = null)
    {
        var response = Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(EmptyBody);
        _server.Given(Request.Create().WithPath(Path).UsingGet())
            .RespondWith(delay is { } d ? response.WithDelay(d) : response);
    }

    [Fact]
    public async Task HealthySource_ReturnsOkWithOneRequest()
    {
        Respond(200);

        var result = await Adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Ok);
        RequestCount.ShouldBe(1);
    }

    [Fact]
    [Trait("Invariant", "5")]
    public async Task SlowSource_TimesOutWithinBudget_AndIsNotRetried()
    {
        Respond(200, TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();

        var result = await Adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Timeout);
        // WireMock does not log requests whose response was aborted (E-001), so prove "no retry" by time:
        // one attempt ends at the 1.0 s attempt timeout; a retry would run on to the 2.0 s total timeout.
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.95));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1.6));
    }

    [Fact]
    [Trait("Invariant", "5")]
    public async Task HangingRetry_IsCutOffByTotalTimeout()
    {
        // First attempt fails fast (503, retryable); the retry then hangs. The total budget must still hold.
        _server.Given(Request.Create().WithPath(Path).UsingGet())
            .InScenario("hang").WillSetStateTo("hanging")
            .RespondWith(Response.Create().WithStatusCode(503));
        _server.Given(Request.Create().WithPath(Path).UsingGet())
            .InScenario("hang").WhenStateIs("hanging")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(EmptyBody).WithDelay(TimeSpan.FromSeconds(5)));
        var stopwatch = Stopwatch.StartNew();

        var result = await Adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Timeout);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2.3));
    }

    [Fact]
    public async Task TransientFailureThenSuccess_IsRetriedOnce()
    {
        _server.Given(Request.Create().WithPath(Path).UsingGet())
            .InScenario("blip").WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(503));
        _server.Given(Request.Create().WithPath(Path).UsingGet())
            .InScenario("blip").WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(EmptyBody));

        var result = await Adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Ok);
        RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task PersistentTransientFailure_IsRetriedAtMostOnce()
    {
        Respond(503);

        var result = await Adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Error);
        result.Error.ShouldBe("http_503");
        RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task InternalServerError_IsNotRetried()
    {
        Respond(500);

        var result = await Adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Error);
        RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task RepeatedFailures_OpenTheCircuit_ThenCallsShortCircuitWithoutHttp()
    {
        Respond(500);
        for (var i = 0; i < 5; i++)
        {
            await Adapter.GetDocumentsAsync(Vin, CancellationToken.None);
        }

        var requestsBefore = RequestCount;
        var result = await Adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.CircuitOpen);
        RequestCount.ShouldBe(requestsBefore);
        _provider.GetRequiredService<ISourceHealth>().GetHealth()
            .Single(h => h.Source == SourceSystem.Sales).Circuit.ShouldBe(CircuitState.Open);
    }

    [Fact]
    public async Task ThreeFailures_OpenTheCircuit_AtTheDefaultMinimumThroughput()
    {
        // D-027: with the old minimum of 5, a source failing at low traffic never tripped the breaker (R4).
        Respond(500);
        for (var i = 0; i < 3; i++)
        {
            (await Adapter.GetDocumentsAsync(Vin, CancellationToken.None)).Status.ShouldBe(SourceStatus.Error);
        }

        var requestsBefore = RequestCount;
        var result = await Adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.CircuitOpen);
        RequestCount.ShouldBe(requestsBefore); // no HTTP call while open
    }

    [Fact]
    public async Task OpenCircuit_AfterBreakDuration_ProbeSuccessClosesIt()
    {
        Respond(500);
        for (var i = 0; i < 5; i++)
        {
            await Adapter.GetDocumentsAsync(Vin, CancellationToken.None);
        }

        _server.Reset();
        Respond(200);
        await Task.Delay(TimeSpan.FromMilliseconds(700)); // > BreakerBreakSeconds (0.5)

        var result = await Adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Ok);
        _provider.GetRequiredService<ISourceHealth>().GetHealth()
            .Single(h => h.Source == SourceSystem.Sales).Circuit.ShouldBe(CircuitState.Closed);
    }

    [Fact]
    public void BothSources_AreRegisteredAsAdapters()
    {
        _provider.GetServices<ISourceAdapter>().Select(a => a.Source)
            .ShouldBe([SourceSystem.Sales, SourceSystem.Service], ignoreOrder: true);
    }

    [Fact]
    public void Health_ReportsEverySourceClosedInitially()
    {
        _provider.GetRequiredService<ISourceHealth>().GetHealth()
            .ShouldAllBe(h => h.Circuit == CircuitState.Closed);
    }
}
