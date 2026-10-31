using DocViewer.Application.Sources;
using DocViewer.Domain;
using DocViewer.Infrastructure.Resilience;
using DocViewer.Infrastructure.Sources.Sales;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using CircuitState = DocViewer.Application.Sources.CircuitState;

namespace DocViewer.Infrastructure.Tests.Resilience.Redis;

/// <summary>
/// D-032 on a real Redis: the Lua window script, the keys, and two DI-built API "replicas" sharing one breaker.
/// </summary>
[Collection(RedisGroup.Name)]
public sealed class SharedCircuitRedisTests(RedisContainer redis) : IAsyncLifetime
{
    private static readonly Vin Vin = Vin.Parse("MA3EWB81S00100001");
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly CircuitWindow Window = new(TimeSpan.FromSeconds(10));

    private readonly WireMockServer _sales = WireMockServer.Start();
    private readonly List<ServiceProvider> _replicas = [];

    public async Task InitializeAsync()
    {
        if (new RedisFactAttribute().Skip is null)
        {
            await redis.ResetAsync();
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var replica in _replicas)
        {
            await replica.DisposeAsync();
        }

        _sales.Stop();
        _sales.Dispose();
    }

    [RedisFact]
    public async Task Record_SumsTheWindowAcrossBuckets_AndDropsBucketsThatLeftIt()
    {
        await using var connection = new RedisConnection(redis.ConnectionString);
        var store = new RedisCircuitStateStore(connection);

        await store.RecordAsync(SourceSystem.Sales, failed: true, Now, Window);
        await store.RecordAsync(SourceSystem.Sales, failed: false, Now.AddSeconds(3), Window);
        (await store.RecordAsync(SourceSystem.Sales, failed: true, Now.AddSeconds(9), Window)).ShouldBe(new WindowCounts(1, 2));

        // 10.5 s after the first outcome its bucket has left the 10 s window.
        (await store.RecordAsync(SourceSystem.Sales, failed: false, Now.AddSeconds(10.5), Window)).ShouldBe(new WindowCounts(2, 1));
        (await store.RecordAsync(SourceSystem.Service, failed: true, Now, Window)).ShouldBe(new WindowCounts(0, 1)); // per source
    }

    [RedisFact]
    public async Task OpenProbeAndClose_RoundTripThroughRedis()
    {
        await using var connection = new RedisConnection(redis.ConnectionString);
        var store = new RedisCircuitStateStore(connection);
        var until = Now.AddSeconds(30);

        (await store.GetOpenUntilAsync(SourceSystem.Sales)).ShouldBeNull();
        await store.OpenAsync(SourceSystem.Sales, until, Now);
        (await store.GetOpenUntilAsync(SourceSystem.Sales)).ShouldBe(until);

        (await store.TryAcquireProbeAsync(SourceSystem.Sales, TimeSpan.FromSeconds(5), Now)).ShouldBeTrue();
        (await store.TryAcquireProbeAsync(SourceSystem.Sales, TimeSpan.FromSeconds(5), Now)).ShouldBeFalse();

        await store.CloseAsync(SourceSystem.Sales);
        (await store.GetOpenUntilAsync(SourceSystem.Sales)).ShouldBeNull();
        (await store.TryAcquireProbeAsync(SourceSystem.Sales, TimeSpan.FromSeconds(5), Now)).ShouldBeTrue();
    }

    [RedisFact]
    public async Task WarmUp_RunsEveryCommand_AndLeavesNoKeyOrCircuitBehind()
    {
        await using var connection = new RedisConnection(redis.ConnectionString);
        var store = new RedisCircuitStateStore(connection);
        await store.OpenAsync(SourceSystem.Sales, Now.AddSeconds(30), Now);

        await store.WarmUpAsync();

        var db = await connection.GetDatabaseAsync();
        (await db.KeyExistsAsync("docviewer:breaker:{warmup}:window")).ShouldBeFalse();
        (await db.KeyExistsAsync("docviewer:breaker:{warmup}:probe")).ShouldBeFalse();
        (await store.GetOpenUntilAsync(SourceSystem.Sales)).ShouldBe(Now.AddSeconds(30)); // real circuits untouched
    }

    [RedisFact]
    public void Keys_ShareOneHashTagPerSource()
    {
        RedisCircuitStateStore.Key(SourceSystem.Sales, "open").ShouldBe("docviewer:breaker:{sales}:open");
        RedisCircuitStateStore.Key(SourceSystem.Service, "window").ShouldBe("docviewer:breaker:{service}:window");
    }

    [RedisFact]
    public async Task FailuresSeenByTwoReplicas_OpenTheCircuitOnBoth_WithoutFurtherHttpCalls()
    {
        RespondToSales(500);
        var a = await ReplicaAsync(redis.ConnectionString);
        var b = await ReplicaAsync(redis.ConnectionString);

        (await Sales(a).GetDocumentsAsync(Vin, CancellationToken.None)).Status.ShouldBe(SourceStatus.Error);
        (await Sales(b).GetDocumentsAsync(Vin, CancellationToken.None)).Status.ShouldBe(SourceStatus.Error);
        (await Sales(a).GetDocumentsAsync(Vin, CancellationToken.None)).Status.ShouldBe(SourceStatus.Error);
        var requests = _sales.LogEntries.Count;

        // b saw one failure itself; per process its breaker would still be closed.
        (await Sales(b).GetDocumentsAsync(Vin, CancellationToken.None)).Status.ShouldBe(SourceStatus.CircuitOpen);
        _sales.LogEntries.Count.ShouldBe(requests);

        var observer = await ReplicaAsync(redis.ConnectionString);
        var health = await observer.GetRequiredService<ISourceHealth>().ReadHealthAsync(CancellationToken.None);
        health.Single(h => h.Source == SourceSystem.Sales).Circuit.ShouldBe(CircuitState.Open);
    }

    [RedisFact]
    [Trait("Invariant", "10")]
    public async Task UnreachableRedis_LeavesEachReplicaWithItsOwnWorkingBreaker()
    {
        RespondToSales(500);
        var replica = await ReplicaAsync("localhost:1,connectTimeout=200");

        for (var i = 0; i < 3; i++)
        {
            (await Sales(replica).GetDocumentsAsync(Vin, CancellationToken.None)).Status.ShouldBe(SourceStatus.Error);
        }

        (await Sales(replica).GetDocumentsAsync(Vin, CancellationToken.None)).Status.ShouldBe(SourceStatus.CircuitOpen);
    }

    private void RespondToSales(int status) =>
        _sales.Given(Request.Create().WithPath("/sales/v1/vehicles/MA3EWB81S00100001/documents").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status));

    private static SalesAdapter Sales(ServiceProvider replica) => replica.GetRequiredService<SalesAdapter>();

    /// <summary>One API process's source adapters, configured as in production apart from shorter budgets.</summary>
    private async Task<ServiceProvider> ReplicaAsync(string redisConnection)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sources:Sales:BaseUrl"] = _sales.Url,
            ["Sources:Service:BaseUrl"] = "http://localhost:1",
            ["ConnectionStrings:Redis"] = redisConnection,
            ["Resilience:MaxRetryAttempts"] = "0",
            // Generous budgets: these tests are about sharing, not timing (E-001, E-005). A cold Redis connect plus the
            // first HTTP call once used up the production 2 s source budget.
            ["Resilience:TotalTimeoutSeconds"] = "10",
            ["Resilience:AttemptTimeoutSeconds"] = "5",
            ["Resilience:SharedStateTimeoutMilliseconds"] = "1000",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSourceAdapters().AddSharedCircuitState(configuration);
        var provider = services.BuildServiceProvider();
        _replicas.Add(provider);
        if (!redisConnection.StartsWith("localhost:1", StringComparison.Ordinal))
        {
            await provider.WarmUpSharedCircuitStateAsync(); // as the API does at start-up (E-026)
        }

        return provider;
    }
}
