using DocViewer.Domain;
using DocViewer.Infrastructure.Options;
using DocViewer.Infrastructure.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace DocViewer.Infrastructure.Tests.Resilience;

/// <summary>D-032 with Redis broken: the breaker must keep working on local state and must not pay Redis's cost per call.</summary>
public sealed class FailoverCircuitStateStoreTests
{
    private static readonly DateTimeOffset Until = new(2026, 10, 31, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));
    private readonly ICircuitStateStore _shared = Substitute.For<ICircuitStateStore>();
    private readonly InMemoryCircuitStateStore _local = new();
    private readonly FailoverCircuitStateStore _store;

    public FailoverCircuitStateStoreTests() =>
        _store = new FailoverCircuitStateStore(
            _shared,
            _local,
            MsOptions.Create(new ResilienceOptions { SharedStateTimeoutMilliseconds = 50, SharedStateRetryAfterSeconds = 10 }),
            _time,
            NullLogger<FailoverCircuitStateStore>.Instance);

    [Fact]
    public async Task HealthySharedStore_IsTheOneUsed()
    {
        _shared.GetOpenUntilAsync(SourceSystem.Sales).Returns(Until);

        (await _store.GetOpenUntilAsync(SourceSystem.Sales)).ShouldBe(Until);
        _store.UsingFallback.ShouldBeFalse();
    }

    [Fact]
    [Trait("Invariant", "10")]
    public async Task FailingSharedStore_FallsBackToLocalState_AndSkipsSharedUntilTheRetryTime()
    {
        _shared.OpenAsync(default, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("redis down"));
        _shared.GetOpenUntilAsync(SourceSystem.Sales).Returns(Until.AddDays(1));

        await _store.OpenAsync(SourceSystem.Sales, Until, _time.GetUtcNow()); // fails over and writes locally

        _store.UsingFallback.ShouldBeTrue();
        (await _store.GetOpenUntilAsync(SourceSystem.Sales)).ShouldBe(Until); // local answer, Redis not asked
        await _shared.DidNotReceive().GetOpenUntilAsync(Arg.Any<SourceSystem>());

        _time.Advance(TimeSpan.FromSeconds(10));
        _store.UsingFallback.ShouldBeFalse();
        (await _store.GetOpenUntilAsync(SourceSystem.Sales)).ShouldBe(Until.AddDays(1)); // back on Redis
    }

    [Fact]
    [Trait("Invariant", "5")]
    public async Task HangingSharedStore_IsAbandonedAfterItsBudget()
    {
        _shared.GetOpenUntilAsync(SourceSystem.Sales).Returns(new TaskCompletionSource<DateTimeOffset?>().Task);

        var read = _store.GetOpenUntilAsync(SourceSystem.Sales); // the budget timer is registered before this returns
        _time.Advance(TimeSpan.FromMilliseconds(51));

        // Real-time guard only so a regression fails instead of hanging; the shared call never completes.
        (await read.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBeNull(); // local state: closed
        _store.UsingFallback.ShouldBeTrue();
    }

    [Fact]
    public async Task EveryOperation_FallsBackWithoutThrowing()
    {
        var now = _time.GetUtcNow();
        var window = new CircuitWindow(TimeSpan.FromSeconds(10));
        _shared.RecordAsync(default, default, default, default).ThrowsAsyncForAnyArgs(new TimeoutException());

        (await _store.RecordAsync(SourceSystem.Sales, failed: true, now, window)).ShouldBe(new WindowCounts(0, 1));
        (await _store.TryAcquireProbeAsync(SourceSystem.Sales, TimeSpan.FromSeconds(3), now)).ShouldBeTrue();
        (await _store.TryAcquireProbeAsync(SourceSystem.Sales, TimeSpan.FromSeconds(3), now)).ShouldBeFalse();
        await _store.CloseAsync(SourceSystem.Sales);
        (await _store.TryAcquireProbeAsync(SourceSystem.Sales, TimeSpan.FromSeconds(3), now)).ShouldBeTrue();
    }
}
