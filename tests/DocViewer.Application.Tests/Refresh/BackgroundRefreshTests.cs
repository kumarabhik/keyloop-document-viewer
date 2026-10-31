using DocViewer.Application.Documents;
using DocViewer.Application.Options;
using DocViewer.Application.Persistence;
using DocViewer.Application.Refresh;
using DocViewer.Application.Sources;
using DocViewer.Application.Tests.Documents;
using DocViewer.Application.Tests.Observability;
using DocViewer.Application.Time;
using DocViewer.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static DocViewer.Application.Tests.Documents.TestData;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace DocViewer.Application.Tests.Refresh;

/// <summary>D-033: partial results are searched again in the background until complete, then served from the cache.</summary>
public sealed class BackgroundRefreshTests : IDisposable
{
    private static readonly Vin OtherVin = Vin.Parse("MA3EWB81S00100002");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 4, 30, 0, TimeSpan.Zero));
    private readonly FakeCache _cache = new();
    private readonly FakeSnapshots _snapshots = new();
    private readonly FakeAudit _audit = new();
    private readonly TestMetrics _metrics = new();
    private readonly RefreshOptions _options = new() { FirstDelaySeconds = 5, MaxAttempts = 3 };
    private readonly RefreshScheduler _scheduler;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Service fails until a test sets this.</summary>
    private volatile bool _serviceRecovered;

    public BackgroundRefreshTests() =>
        _scheduler = new RefreshScheduler(MsOptions.Create(_options), _time, _metrics.Metrics, NullLogger<RefreshScheduler>.Instance);

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _scheduler.Dispose();
        _metrics.Dispose();
    }

    private FakeAdapter RecoveringService() => new(SourceSystem.Service, _ => Task.FromResult(_serviceRecovered
        ? SourceResult.Ok(SourceSystem.Service, [Doc("JC-1", SourceSystem.Service)], TimeSpan.FromMilliseconds(10))
        : SourceResult.Failed(SourceSystem.Service, SourceStatus.Timeout, "timeout", TimeSpan.FromMilliseconds(10))));

    private (AggregationService Service, BackgroundRefresher Refresher) Create(FakeAdapter? service = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISourceAdapter>(FakeAdapter.Returning(SourceSystem.Sales, Doc("INV-1", SourceSystem.Sales)));
        services.AddSingleton<ISourceAdapter>(service ?? RecoveringService());
        var rules = MsOptions.Create(new BusinessRulesOptions());
        var aggregation = new AggregationService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _cache,
            _snapshots,
            _audit,
            _scheduler,
            new SourceActivity(),
            _metrics.Metrics,
            new DealershipCalendar(_time, rules),
            rules,
            MsOptions.Create(new LatencyBudgetOptions()),
            _time,
            NullLogger<AggregationService>.Instance);
        var refresher = new BackgroundRefresher(
            _scheduler, aggregation, MsOptions.Create(_options), _metrics.Metrics, NullLogger<BackgroundRefresher>.Instance);
        return (aggregation, refresher);
    }

    private static SearchRequest Search(Vin? vin = null) => new(vin ?? TestVin, Refresh: false, "trace-1");

    [Fact]
    public async Task PartialSearch_QueuesTheVinOnce()
    {
        using var jobs = _metrics.Collect<long>("docviewer.refresh.jobs");
        var (service, _) = Create();

        (await service.SearchAsync(Search(), CancellationToken.None)).Documents.Partial.ShouldBeTrue();
        await service.SearchAsync(Search(), CancellationToken.None);

        _scheduler.Pending.ShouldBe(1);
        jobs.GetMeasurementSnapshot().Select(m => m.Tags["outcome"]).ShouldBe(["enqueued"]);
    }

    [Fact]
    public async Task CompleteSearch_QueuesNothing()
    {
        _serviceRecovered = true;
        var (service, _) = Create();

        await service.SearchAsync(Search(), CancellationToken.None);

        _scheduler.Pending.ShouldBe(0);
    }

    [Fact]
    public async Task QueuedJob_IsReleasedOnlyAfterItsDelay()
    {
        var (service, _) = Create();
        await service.SearchAsync(Search(), CancellationToken.None);
        await using var due = _scheduler.ReadAllAsync(_stop.Token).GetAsyncEnumerator();
        var next = due.MoveNextAsync().AsTask();

        _time.Advance(TimeSpan.FromSeconds(4.9));
        next.IsCompleted.ShouldBeFalse();
        _time.Advance(TimeSpan.FromSeconds(0.2));

        (await next.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        due.Current.ShouldBe(new RefreshJob(TestVin, 1));
    }

    [Fact]
    public async Task Refresh_AfterTheSourceRecovers_CachesTheCompleteResult_SoTheNextSearchIsACompleteHit()
    {
        using var jobs = _metrics.Collect<long>("docviewer.refresh.jobs");
        var (service, refresher) = Create();
        (await service.SearchAsync(Search(), CancellationToken.None)).Documents.Partial.ShouldBeTrue();
        _cache.Entries.ShouldBeEmpty(); // invariant 4: the partial result was not cached

        _serviceRecovered = true;
        await refresher.ProcessAsync(new RefreshJob(TestVin, 1), CancellationToken.None);

        var next = await service.SearchAsync(Search(), CancellationToken.None);
        next.Cache.ShouldBe(CacheStatus.Hit);
        next.Documents.Partial.ShouldBeFalse();
        next.Documents.Documents.Select(d => d.Source).ShouldBe([SourceSystem.Sales, SourceSystem.Service], ignoreOrder: true);
        _scheduler.Pending.ShouldBe(0);
        jobs.GetMeasurementSnapshot().Select(m => m.Tags["outcome"]).ShouldBe(["enqueued", "completed"]);
    }

    [Fact]
    public async Task StillPartial_IsRetriedThreeTimesLongerLater_ThenGivenUp()
    {
        using var jobs = _metrics.Collect<long>("docviewer.refresh.jobs");
        var (service, refresher) = Create();
        await service.SearchAsync(Search(), CancellationToken.None);
        await using var due = _scheduler.ReadAllAsync(_stop.Token).GetAsyncEnumerator();
        _time.Advance(TimeSpan.FromSeconds(5));
        (await due.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        await refresher.ProcessAsync(due.Current, CancellationToken.None);
        var second = due.MoveNextAsync().AsTask();
        _time.Advance(TimeSpan.FromSeconds(14.9));
        second.IsCompleted.ShouldBeFalse();
        _time.Advance(TimeSpan.FromSeconds(0.2));
        (await second.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        due.Current.ShouldBe(new RefreshJob(TestVin, 2));

        await refresher.ProcessAsync(new RefreshJob(TestVin, 3), CancellationToken.None);

        _scheduler.Pending.ShouldBe(0); // a later partial search may queue it again
        jobs.GetMeasurementSnapshot().Select(m => m.Tags["outcome"]).ShouldBe(["enqueued", "retried", "gave_up"]);
    }

    [Fact]
    public async Task RefreshFanOut_IsNotCountedAsAPartialResponse()
    {
        using var partial = _metrics.Collect<long>("docviewer.partial.responses");
        var (service, refresher) = Create();
        await service.SearchAsync(Search(), CancellationToken.None);

        await refresher.ProcessAsync(new RefreshJob(TestVin, 1), CancellationToken.None); // still partial

        partial.GetMeasurementSnapshot().Count.ShouldBe(1); // the user's response only
    }

    [Fact]
    public async Task FullQueue_DropsNewVins()
    {
        _options.QueueCapacity = 1;
        using var jobs = _metrics.Collect<long>("docviewer.refresh.jobs");
        var (service, _) = Create();

        await service.SearchAsync(Search(), CancellationToken.None);
        await service.SearchAsync(Search(OtherVin), CancellationToken.None);

        _scheduler.Pending.ShouldBe(1);
        jobs.GetMeasurementSnapshot().Select(m => m.Tags["outcome"]).ShouldBe(["enqueued", "dropped"]);
    }

    [Fact]
    public async Task Disabled_QueuesNothing()
    {
        _options.Enabled = false;
        var (service, _) = Create();

        await service.SearchAsync(Search(), CancellationToken.None);

        _scheduler.Pending.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_RefreshesDueJobs_UntilStopped()
    {
        var (service, refresher) = Create();
        var running = refresher.RunAsync(_stop.Token);
        await service.SearchAsync(Search(), CancellationToken.None);
        _serviceRecovered = true;

        _time.Advance(TimeSpan.FromSeconds(5));
        SpinWait.SpinUntil(() => _cache.Entries.ContainsKey(TestVin), TimeSpan.FromSeconds(10)).ShouldBeTrue();

        await _stop.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(running);
    }

    [Fact]
    public async Task ThrowingAdapter_LeavesTheResultPartial_SoTheJobIsRetriedNotLost()
    {
        var (_, refresher) = Create(new FakeAdapter(SourceSystem.Service, _ => throw new InvalidOperationException("bug")));
        _scheduler.TryEnqueue(TestVin);

        await refresher.ProcessAsync(new RefreshJob(TestVin, 1), CancellationToken.None);

        _scheduler.Pending.ShouldBe(1); // retried, not lost
    }
}
