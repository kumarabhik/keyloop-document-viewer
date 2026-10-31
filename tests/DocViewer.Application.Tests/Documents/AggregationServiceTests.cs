using DocViewer.Application.Documents;
using DocViewer.Application.Observability;
using DocViewer.Application.Options;
using DocViewer.Application.Persistence;
using DocViewer.Application.Sources;
using DocViewer.Application.Tests.Observability;
using DocViewer.Application.Time;
using DocViewer.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static DocViewer.Application.Tests.Documents.TestData;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace DocViewer.Application.Tests.Documents;

public sealed class AggregationServiceTests : IDisposable
{
    // 2026-10-27 10:00 IST
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 4, 30, 0, TimeSpan.Zero));
    private readonly FakeCache _cache = new();
    private readonly FakeSnapshots _snapshots = new();
    private readonly FakeAudit _audit = new();
    private readonly SourceActivity _activity = new();
    private readonly TestMetrics _metrics = new();

    public void Dispose() => _metrics.Dispose();

    private AggregationService CreateService(params ISourceAdapter[] adapters)
    {
        var services = new ServiceCollection();
        foreach (var adapter in adapters)
        {
            services.AddSingleton(adapter);
        }

        var rules = MsOptions.Create(new BusinessRulesOptions());
        return new AggregationService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _cache,
            _snapshots,
            _audit,
            _activity,
            _metrics.Metrics,
            new DealershipCalendar(_time, rules),
            rules,
            MsOptions.Create(new LatencyBudgetOptions()),
            _time,
            NullLogger<AggregationService>.Instance);
    }

    private static SearchRequest Request(bool refresh = false) => new(TestVin, refresh, "trace-1");

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(5);

    /// <summary>
    /// The loop may notice completion a few steps late (continuations hop threads). The fakes never answer, so finishing
    /// at all on fake time proves the budget; the lower bound proves it did not give up early.
    /// </summary>
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Moves the fake clock forward in small steps until <paramref name="task"/> finishes and returns how much fake time
    /// that took. Budgeted calls start on the thread pool, so a single big jump could land before a timer exists;
    /// stepping makes the result independent of scheduling. The real-time guard only stops a broken test from hanging.
    /// </summary>
    private async Task<TimeSpan> AdvanceUntilDoneAsync(Task task)
    {
        var waited = TimeSpan.Zero;
        var guard = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && guard.Elapsed < TimeSpan.FromSeconds(10))
        {
            _time.Advance(Step);
            waited += Step;
            await Task.Yield();
            SpinWait.SpinUntil(() => task.IsCompleted, 1); // let thread-pool continuations register their timers
        }

        task.IsCompleted.ShouldBeTrue("the search never finished: a call is not under any budget");
        return waited;
    }

    [Fact]
    public async Task Search_BothSourcesOk_ReturnsMergedCompleteResult()
    {
        var service = CreateService(
            FakeAdapter.Returning(SourceSystem.Sales, Doc("S-1", SourceSystem.Sales, "2025-01-01")),
            FakeAdapter.Returning(SourceSystem.Service, Doc("V-1", SourceSystem.Service, "2025-06-01")));

        var result = await service.SearchAsync(Request(), CancellationToken.None);

        result.Outcome.ShouldBe(SearchOutcome.Complete);
        result.Cache.ShouldBe(CacheStatus.Miss);
        result.Documents.Partial.ShouldBeFalse();
        result.Documents.Documents.Select(d => d.Id).ShouldBe(["SERVICE:V-1", "SALES:S-1"]); // date desc
        result.Documents.Sources.Select(s => s.Source).ShouldBe([SourceSystem.Sales, SourceSystem.Service]);
        result.Documents.GeneratedAt.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public async Task Search_ComputesStatusInDealershipTimezone()
    {
        var service = CreateService(
            FakeAdapter.Returning(SourceSystem.Sales, Doc("S-1", SourceSystem.Sales, expiry: "2026-10-26")),
            FakeAdapter.Returning(SourceSystem.Service, Doc("V-1", SourceSystem.Service, expiry: "2026-11-06")));

        var result = await service.SearchAsync(Request(), CancellationToken.None);

        result.Documents.Documents.Single(d => d.SourceId == "S-1").Status.ShouldBe(DocumentStatus.Expired);
        result.Documents.Documents.Single(d => d.SourceId == "V-1").Status.ShouldBe(DocumentStatus.ExpiringSoon);
    }

    [Fact]
    [Trait("Invariant", "1")]
    [Trait("Invariant", "3")]
    public async Task Search_WhenSalesDown_ReturnsPartialWithServiceDocuments()
    {
        var service = CreateService(
            FakeAdapter.Failing(SourceSystem.Sales, SourceStatus.Error, "http_500"),
            FakeAdapter.Returning(SourceSystem.Service, Doc("V-1", SourceSystem.Service)));

        var result = await service.SearchAsync(Request(), CancellationToken.None);

        result.Outcome.ShouldBe(SearchOutcome.Partial);
        result.Documents.Partial.ShouldBeTrue();
        result.Documents.Documents.Select(d => d.Id).ShouldBe(["SERVICE:V-1"]);
        var sales = result.Documents.Sources.Single(s => s.Source == SourceSystem.Sales);
        sales.Status.ShouldBe(SourceStatus.Error);
        sales.Error.ShouldBe("http_500");
    }

    [Fact]
    [Trait("Invariant", "1")]
    public async Task Search_WhenServiceTimesOut_ReturnsPartial()
    {
        var service = CreateService(
            FakeAdapter.Returning(SourceSystem.Sales, Doc("S-1", SourceSystem.Sales)),
            FakeAdapter.Failing(SourceSystem.Service, SourceStatus.Timeout, "timeout"));

        var result = await service.SearchAsync(Request(), CancellationToken.None);

        result.Outcome.ShouldBe(SearchOutcome.Partial);
        result.Documents.Sources.Single(s => s.Source == SourceSystem.Service).Status.ShouldBe(SourceStatus.Timeout);
    }

    [Fact]
    [Trait("Invariant", "1")]
    public async Task Search_WhenAdapterThrows_TreatsItAsSourceError()
    {
        var service = CreateService(
            new FakeAdapter(SourceSystem.Sales, _ => throw new InvalidOperationException("adapter bug")),
            FakeAdapter.Returning(SourceSystem.Service, Doc("V-1", SourceSystem.Service)));

        var result = await service.SearchAsync(Request(), CancellationToken.None);

        result.Outcome.ShouldBe(SearchOutcome.Partial);
        result.Documents.Sources.Single(s => s.Source == SourceSystem.Sales).Error.ShouldBe("unexpected");
    }

    [Fact]
    public async Task Search_BothDownWithSnapshot_ServesStaleLabelledData()
    {
        var fetchedAt = _time.GetUtcNow().AddHours(-3);
        _snapshots.Stored[(TestVin, SourceSystem.Service)] =
            new SourceSnapshot(SourceSystem.Service, [Doc("V-OLD", SourceSystem.Service, expiry: "2026-10-01")], fetchedAt);
        var service = CreateService(
            FakeAdapter.Failing(SourceSystem.Sales, SourceStatus.CircuitOpen, "circuit_open"),
            FakeAdapter.Failing(SourceSystem.Service, SourceStatus.Timeout, "timeout"));

        var result = await service.SearchAsync(Request(), CancellationToken.None);

        result.Outcome.ShouldBe(SearchOutcome.Partial);
        var serviceSource = result.Documents.Sources.Single(s => s.Source == SourceSystem.Service);
        serviceSource.Status.ShouldBe(SourceStatus.Stale);
        serviceSource.AsOf.ShouldBe(fetchedAt);
        serviceSource.Error.ShouldBe("timeout"); // why it is stale stays visible
        result.Documents.Sources.Single(s => s.Source == SourceSystem.Sales).Status.ShouldBe(SourceStatus.CircuitOpen);
        // Status is recomputed for today, not frozen at snapshot time.
        result.Documents.Documents.Single().Status.ShouldBe(DocumentStatus.Expired);
    }

    [Fact]
    [Trait("Invariant", "5")]
    public async Task Search_SnapshotReadHangs_GivesUpAfterItsBudget_AndReportsTheSourceAsFailed()
    {
        _snapshots.ReadsHang = true;
        var service = CreateService(
            FakeAdapter.Returning(SourceSystem.Sales, Doc("S-1", SourceSystem.Sales)),
            FakeAdapter.Failing(SourceSystem.Service, SourceStatus.Timeout, "timeout"));

        var search = service.SearchAsync(Request(refresh: true), CancellationToken.None);
        var waited = await AdvanceUntilDoneAsync(search);

        // E-018: used to wait as long as the database did (~12 s measured).
        waited.ShouldBeInRange(LatencyBudgetOptions.DefaultSnapshotReadTimeout, LatencyBudgetOptions.DefaultSnapshotReadTimeout + Slack);
        var result = await search;
        result.Outcome.ShouldBe(SearchOutcome.Partial);
        var service2 = result.Documents.Sources.Single(s => s.Source == SourceSystem.Service);
        service2.Status.ShouldBe(SourceStatus.Timeout); // no snapshot in time: the original failure stands
        result.Documents.Documents.Select(d => d.SourceId).ShouldBe(["S-1"]);
    }

    [Fact]
    [Trait("Invariant", "5")]
    [Trait("Invariant", "10")]
    public async Task Search_CacheHangs_ReadAndWriteEachGiveUpAfterTheirBudget()
    {
        _cache.Hangs = true;
        var service = CreateService(
            FakeAdapter.Returning(SourceSystem.Sales, Doc("S-1", SourceSystem.Sales)),
            FakeAdapter.Returning(SourceSystem.Service, Doc("V-1", SourceSystem.Service)));

        var search = service.SearchAsync(Request(), CancellationToken.None);
        var waited = await AdvanceUntilDoneAsync(search);

        // E-019: used to wait ~5-6 s on each call to a stopped Redis. Read and write each get one budget.
        var both = LatencyBudgetOptions.DefaultCacheTimeout * 2;
        waited.ShouldBeInRange(both, both + Slack);
        var result = await search;
        result.Outcome.ShouldBe(SearchOutcome.Complete);
        result.Cache.ShouldBe(CacheStatus.Miss);
        result.Documents.Documents.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Search_BothDownNoSnapshot_IsAllSourcesFailed()
    {
        var service = CreateService(
            FakeAdapter.Failing(SourceSystem.Sales),
            FakeAdapter.Failing(SourceSystem.Service, SourceStatus.Timeout, "timeout"));

        var result = await service.SearchAsync(Request(), CancellationToken.None);

        result.Outcome.ShouldBe(SearchOutcome.Failed);
        result.Documents.Documents.ShouldBeEmpty();
        _cache.Sets.ShouldBe(0);
    }

    [Fact]
    [Trait("Invariant", "4")]
    public async Task Search_PartialResult_IsNotCached()
    {
        var service = CreateService(
            FakeAdapter.Returning(SourceSystem.Sales, Doc("S-1", SourceSystem.Sales)),
            FakeAdapter.Failing(SourceSystem.Service, SourceStatus.Timeout, "timeout"));

        await service.SearchAsync(Request(), CancellationToken.None);

        _cache.Sets.ShouldBe(0);
        _cache.Entries.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Invariant", "4")]
    public async Task Search_StaleResult_IsNotCached()
    {
        _snapshots.Stored[(TestVin, SourceSystem.Service)] =
            new SourceSnapshot(SourceSystem.Service, [Doc("V-OLD", SourceSystem.Service)], _time.GetUtcNow());
        var service = CreateService(
            FakeAdapter.Returning(SourceSystem.Sales),
            FakeAdapter.Failing(SourceSystem.Service));

        await service.SearchAsync(Request(), CancellationToken.None);

        _cache.Sets.ShouldBe(0);
    }

    [Fact]
    public async Task Search_CompleteResult_IsCachedAndServedOnNextCall()
    {
        var sales = FakeAdapter.Returning(SourceSystem.Sales, Doc("S-1", SourceSystem.Sales));
        var serviceAdapter = FakeAdapter.Returning(SourceSystem.Service);
        var service = CreateService(sales, serviceAdapter);

        var first = await service.SearchAsync(Request(), CancellationToken.None);
        var second = await service.SearchAsync(Request(), CancellationToken.None);

        first.Cache.ShouldBe(CacheStatus.Miss);
        second.Cache.ShouldBe(CacheStatus.Hit);
        second.Documents.ShouldBeSameAs(first.Documents);
        sales.Calls.ShouldBe(1);
        serviceAdapter.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Search_Refresh_BypassesCacheReadButStillWrites()
    {
        var sales = FakeAdapter.Returning(SourceSystem.Sales);
        var service = CreateService(sales, FakeAdapter.Returning(SourceSystem.Service));
        await service.SearchAsync(Request(), CancellationToken.None);

        var refreshed = await service.SearchAsync(Request(refresh: true), CancellationToken.None);

        refreshed.Cache.ShouldBe(CacheStatus.Bypass);
        sales.Calls.ShouldBe(2);
        _cache.Sets.ShouldBe(2);
    }

    [Fact]
    public async Task Search_EmptyFromBothSources_IsCompleteNotFailed()
    {
        var service = CreateService(FakeAdapter.Returning(SourceSystem.Sales), FakeAdapter.Returning(SourceSystem.Service));

        var result = await service.SearchAsync(Request(), CancellationToken.None);

        result.Outcome.ShouldBe(SearchOutcome.Complete); // D-004
        result.Documents.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_CallsSourcesConcurrently()
    {
        // Each adapter waits until the other has started. Sequential calls would never get past the first.
        var salesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serviceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(
            new FakeAdapter(SourceSystem.Sales, async _ =>
            {
                salesStarted.SetResult();
                await serviceStarted.Task;
                return SourceResult.Ok(SourceSystem.Sales, [], TimeSpan.Zero);
            }),
            new FakeAdapter(SourceSystem.Service, async _ =>
            {
                serviceStarted.SetResult();
                await salesStarted.Task;
                return SourceResult.Ok(SourceSystem.Service, [], TimeSpan.Zero);
            }));

        var result = await service.SearchAsync(Request(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        result.Outcome.ShouldBe(SearchOutcome.Complete);
    }

    [Fact]
    public async Task Search_ConcurrentMissesForOneVin_ShareOneFanOut()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sales = new FakeAdapter(SourceSystem.Sales, async _ =>
        {
            await gate.Task;
            return SourceResult.Ok(SourceSystem.Sales, [], TimeSpan.Zero);
        });
        var service = CreateService(sales, FakeAdapter.Returning(SourceSystem.Service));
        using var cacheLookups = _metrics.Collect<long>("docviewer.cache.requests");

        var searches = Enumerable.Range(0, 5).Select(_ => service.SearchAsync(Request(), CancellationToken.None)).ToList();
        // Cache calls run on the thread pool (E-019), so wait until all five have missed the cache: the miss is recorded
        // right before joining the fan-out, with no await between. Only then are they truly concurrent with it.
        SpinWait.SpinUntil(() => cacheLookups.GetMeasurementSnapshot().Count(m => Equals(m.Tags["result"], "miss")) == 5, TimeSpan.FromSeconds(5))
            .ShouldBeTrue();
        gate.SetResult();
        var results = await Task.WhenAll(searches).WaitAsync(TimeSpan.FromSeconds(5));

        sales.Calls.ShouldBe(1);
        results.Select(r => r.Documents).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Search_AfterFanOutCompletes_NextMissFansOutAgain()
    {
        var sales = FakeAdapter.Failing(SourceSystem.Sales); // partial ⇒ not cached ⇒ next call is a miss
        var service = CreateService(sales, FakeAdapter.Returning(SourceSystem.Service));

        await service.SearchAsync(Request(), CancellationToken.None);
        await service.SearchAsync(Request(), CancellationToken.None);

        sales.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Search_CallerCancels_StopsWaitingWithoutCancellingSharedFanOut()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sales = new FakeAdapter(SourceSystem.Sales, async _ =>
        {
            await gate.Task;
            return SourceResult.Ok(SourceSystem.Sales, [], TimeSpan.Zero);
        });
        var service = CreateService(sales, FakeAdapter.Returning(SourceSystem.Service));
        using var cts = new CancellationTokenSource();
        using var cacheLookups = _metrics.Collect<long>("docviewer.cache.requests");

        var cancelled = service.SearchAsync(Request(), cts.Token);
        var patient = service.SearchAsync(Request(), CancellationToken.None);
        // Both must be waiting on the shared fan-out before one cancels (E-020: cache calls hop threads).
        SpinWait.SpinUntil(() => cacheLookups.GetMeasurementSnapshot().Count(m => Equals(m.Tags["result"], "miss")) == 2, TimeSpan.FromSeconds(5))
            .ShouldBeTrue();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => cancelled);
        gate.SetResult();
        (await patient).Outcome.ShouldBe(SearchOutcome.Complete);
        sales.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Search_SavesSnapshotsOnlyForLiveSources()
    {
        var service = CreateService(
            FakeAdapter.Returning(SourceSystem.Sales, Doc("S-1", SourceSystem.Sales)),
            FakeAdapter.Failing(SourceSystem.Service));

        await service.SearchAsync(Request(), CancellationToken.None);
        await _snapshots.WaitForSavesAsync(1);

        _snapshots.Stored.Keys.ShouldBe([(TestVin, SourceSystem.Sales)]);
    }

    [Fact]
    public async Task Search_WritesOneAuditRowPerSearch()
    {
        var service = CreateService(FakeAdapter.Returning(SourceSystem.Sales), FakeAdapter.Failing(SourceSystem.Service));

        await service.SearchAsync(Request(), CancellationToken.None);
        var entry = await _audit.WaitForEntryAsync();

        entry.TraceId.ShouldBe("trace-1");
        entry.Outcome.ShouldBe(SearchOutcome.Partial);
        entry.CacheHit.ShouldBeFalse();
        entry.SourceStatuses[SourceSystem.Service].ShouldBe(SourceStatus.Error);
    }

    [Fact]
    [Trait("Invariant", "10")]
    public async Task Search_SlowAuditWrite_DoesNotDelayResponse()
    {
        var neverFinishes = new TaskCompletionSource();
        _audit.OnWrite = () => neverFinishes.Task;
        var service = CreateService(FakeAdapter.Returning(SourceSystem.Sales), FakeAdapter.Returning(SourceSystem.Service));

        var result = await service.SearchAsync(Request(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        result.Outcome.ShouldBe(SearchOutcome.Complete);
    }

    [Fact]
    public async Task RecordInvalidSearch_WritesInvalidAuditRow()
    {
        var service = CreateService();

        service.RecordInvalidSearch("BADVIN", "trace-2");
        var entry = await _audit.WaitForEntryAsync();

        entry.Outcome.ShouldBe(SearchOutcome.Invalid);
        entry.VinInput.ShouldBe("BADVIN");
    }

    [Fact]
    public async Task Search_RecordsLiveOutcomePerSourceForHealthPanel()
    {
        _snapshots.Stored[(TestVin, SourceSystem.Service)] =
            new SourceSnapshot(SourceSystem.Service, [], _time.GetUtcNow());
        var service = CreateService(FakeAdapter.Returning(SourceSystem.Sales), FakeAdapter.Failing(SourceSystem.Service, SourceStatus.Timeout, "timeout"));

        await service.SearchAsync(Request(), CancellationToken.None);

        _activity.LastFor(SourceSystem.Sales)!.Status.ShouldBe(SourceStatus.Ok);
        // The live outcome, not the stale stand-in: the panel shows what the source actually did.
        _activity.LastFor(SourceSystem.Service)!.Status.ShouldBe(SourceStatus.Timeout);
        _activity.LastFor(SourceSystem.Service)!.At.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public async Task Search_RecordsSourceCacheAndDegradationMetrics()
    {
        _snapshots.Stored[(TestVin, SourceSystem.Service)] = new SourceSnapshot(SourceSystem.Service, [], _time.GetUtcNow());
        using var sourceRequests = _metrics.Collect<long>("docviewer.source.requests");
        using var duration = _metrics.Collect<double>("docviewer.source.duration");
        using var cacheLookups = _metrics.Collect<long>("docviewer.cache.requests");
        using var partial = _metrics.Collect<long>("docviewer.partial.responses");
        using var stale = _metrics.Collect<long>("docviewer.stale.served");
        var service = CreateService(FakeAdapter.Returning(SourceSystem.Sales), FakeAdapter.Failing(SourceSystem.Service, SourceStatus.CircuitOpen, "circuit_open"));

        await service.SearchAsync(Request(), CancellationToken.None);
        await service.SearchAsync(Request(refresh: true), CancellationToken.None);

        sourceRequests.GetMeasurementSnapshot().Select(m => $"{m.Tags["source"]}:{m.Tags["outcome"]}").Order(StringComparer.Ordinal)
            .ShouldBe(["sales:ok", "sales:ok", "service:circuit_open", "service:circuit_open"]);
        duration.GetMeasurementSnapshot().Count.ShouldBe(4);
        cacheLookups.GetMeasurementSnapshot().Select(m => m.Tags["result"]).ShouldBe(["miss", "bypass"]);
        partial.GetMeasurementSnapshot().ShouldAllBe(m => (string)m.Tags["failed_source"]! == "service");
        partial.GetMeasurementSnapshot().Count.ShouldBe(2);
        stale.GetMeasurementSnapshot().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Search_CacheHit_IsCounted()
    {
        using var cacheLookups = _metrics.Collect<long>("docviewer.cache.requests");
        var service = CreateService(FakeAdapter.Returning(SourceSystem.Sales), FakeAdapter.Returning(SourceSystem.Service));

        await service.SearchAsync(Request(), CancellationToken.None);
        await service.SearchAsync(Request(), CancellationToken.None);

        cacheLookups.GetMeasurementSnapshot().Select(m => m.Tags["result"]).ShouldBe(["miss", "hit"]);
    }

    [Fact]
    [Trait("Invariant", "7")]
    public void Metrics_CircuitGauge_ReportsEachSourceWithoutVin()
    {
        var health = new StubHealth([new(SourceSystem.Sales, CircuitState.Open), new(SourceSystem.Service, CircuitState.HalfOpen)]);
        using var metrics = new TestMetrics(health);
        using var gauge = metrics.Collect<int>("docviewer.circuit.state");

        gauge.RecordObservableInstruments();

        gauge.GetMeasurementSnapshot().Select(m => $"{m.Tags["source"]}={m.Value}").ShouldBe(["sales=2", "service=1"]);
        gauge.GetMeasurementSnapshot().ShouldAllBe(m => m.Tags.Count == 1 && m.Tags.ContainsKey("source"));
    }

    private sealed class StubHealth(IReadOnlyList<SourceHealth> rows) : ISourceHealth
    {
        public IReadOnlyList<SourceHealth> GetHealth() => rows;

        public Task<IReadOnlyList<SourceHealth>> ReadHealthAsync(CancellationToken cancellationToken) => Task.FromResult(GetHealth());
    }
}
