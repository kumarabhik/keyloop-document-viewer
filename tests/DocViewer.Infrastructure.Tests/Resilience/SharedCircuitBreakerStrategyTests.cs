using System.Net;
using DocViewer.Application.Sources;
using DocViewer.Domain;
using DocViewer.Infrastructure.Options;
using DocViewer.Infrastructure.Resilience;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Polly.CircuitBreaker;
using CircuitState = DocViewer.Application.Sources.CircuitState;

namespace DocViewer.Infrastructure.Tests.Resilience;

/// <summary>
/// D-032: the breaker's rules with its state in a store. Each <see cref="Replica"/> is one API process; replicas built
/// on the same store share one window, one open circuit and one probe, as replicas sharing Redis do.
/// </summary>
public sealed class SharedCircuitBreakerStrategyTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryCircuitStateStore _store = new();
    private readonly ResilienceOptions _options = new()
    {
        BreakerFailureRatio = 0.5,
        BreakerMinimumThroughput = 3,
        BreakerSamplingSeconds = 10,
        BreakerBreakSeconds = 30,
    };

    private Replica NewReplica() => new(_store, _options, _time);

    [Fact]
    public async Task FailuresSpreadAcrossReplicas_OpenTheCircuitForAllOfThem()
    {
        var a = NewReplica();
        var b = NewReplica();

        (await a.CallAsync(HttpStatusCode.InternalServerError)).ShouldBe(Call.Passed);
        (await b.CallAsync(HttpStatusCode.InternalServerError)).ShouldBe(Call.Passed);
        (await a.CallAsync(HttpStatusCode.InternalServerError)).ShouldBe(Call.Passed); // 3rd failure in the shared window

        (await b.CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Rejected); // b saw only one failure itself
        b.Calls.ShouldBe(1);
        (await NewReplica().CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Rejected); // and a replica that saw none
        b.Registry.GetHealth().Single(h => h.Source == SourceSystem.Sales).Circuit.ShouldBe(CircuitState.Open);
    }

    [Fact]
    public async Task EachReplicaAlone_BelowMinimumThroughput_ButSharedWindowReachesIt()
    {
        // Two replicas, two failures each: per process (before D-032) neither breaker would have seen 3 calls.
        var a = NewReplica();
        var b = NewReplica();
        await a.CallAsync(HttpStatusCode.ServiceUnavailable);
        await b.CallAsync(HttpStatusCode.ServiceUnavailable);
        (await b.CallAsync(HttpStatusCode.ServiceUnavailable)).ShouldBe(Call.Passed);

        (await a.CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Rejected);
    }

    [Fact]
    public async Task FewerCallsThanMinimumThroughput_KeepItClosed()
    {
        var a = NewReplica();
        await a.CallAsync(HttpStatusCode.InternalServerError);
        await a.CallAsync(HttpStatusCode.InternalServerError);

        (await NewReplica().CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Passed);
    }

    [Fact]
    public async Task FailureRatioBelowThreshold_KeepsItClosed()
    {
        var a = NewReplica();
        await a.CallAsync(HttpStatusCode.OK);
        await a.CallAsync(HttpStatusCode.OK);
        await a.CallAsync(HttpStatusCode.OK);
        await a.CallAsync(HttpStatusCode.InternalServerError);
        await a.CallAsync(HttpStatusCode.InternalServerError); // 2 of 5 = 0.4 < 0.5

        (await a.CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Passed);
    }

    [Fact]
    public async Task FailuresOlderThanTheSamplingWindow_NoLongerCount()
    {
        var a = NewReplica();
        await a.CallAsync(HttpStatusCode.InternalServerError);
        await a.CallAsync(HttpStatusCode.InternalServerError);
        _time.Advance(TimeSpan.FromSeconds(11));
        await a.CallAsync(HttpStatusCode.InternalServerError); // only 1 failure inside the last 10 s

        (await a.CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Passed);
    }

    [Fact]
    public async Task NonTransientResponses_AreNotFailures()
    {
        var a = NewReplica();
        for (var i = 0; i < 5; i++)
        {
            await a.CallAsync(HttpStatusCode.NotFound);
        }

        (await a.CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Passed);
    }

    [Fact]
    public async Task AfterTheBreak_OnlyOneReplicaProbes_AndItsSuccessClosesTheCircuitForAll()
    {
        var a = NewReplica();
        var b = NewReplica();
        await OpenAsync(a);
        _time.Advance(TimeSpan.FromSeconds(31));

        var gate = new TaskCompletionSource<HttpResponseMessage>();
        var probe = a.CallAsync(() => gate.Task);
        (await b.CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Rejected); // half-open: a holds the only probe
        b.Registry.GetHealth().Single(h => h.Source == SourceSystem.Sales).Circuit.ShouldBe(CircuitState.HalfOpen);

        gate.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        (await probe).ShouldBe(Call.Passed);

        (await b.CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Passed);
        (await b.Registry.ReadHealthAsync(CancellationToken.None))
            .Single(h => h.Source == SourceSystem.Sales).Circuit.ShouldBe(CircuitState.Closed);
    }

    [Fact]
    public async Task FailedProbe_ReopensTheCircuitForAnotherBreak()
    {
        var a = NewReplica();
        await OpenAsync(a);
        _time.Advance(TimeSpan.FromSeconds(31));

        (await a.CallAsync(HttpStatusCode.ServiceUnavailable)).ShouldBe(Call.Passed); // the probe
        (await NewReplica().CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Rejected);

        _time.Advance(TimeSpan.FromSeconds(29));
        (await NewReplica().CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Rejected); // the new break counts from the probe
        _time.Advance(TimeSpan.FromSeconds(2));
        (await NewReplica().CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Passed);
    }

    [Fact]
    public async Task ClosingAfterAProbe_StartsWithAnEmptyWindow()
    {
        var a = NewReplica();
        await OpenAsync(a);
        _time.Advance(TimeSpan.FromSeconds(31));
        await a.CallAsync(HttpStatusCode.OK); // probe closes it

        await a.CallAsync(HttpStatusCode.InternalServerError); // one failure is not 3 calls, whatever came before

        (await a.CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Passed);
    }

    [Fact]
    public async Task CallerCancellation_IsNotCountedAsASourceFailure()
    {
        var a = NewReplica();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        for (var i = 0; i < 5; i++)
        {
            await a.CallAsync(() => Task.FromCanceled<HttpResponseMessage>(cancelled.Token), cancelled.Token);
        }

        (await a.CallAsync(HttpStatusCode.OK)).ShouldBe(Call.Passed);
    }

    [Fact]
    public async Task ReadHealth_ShowsACircuitAnotherReplicaOpened()
    {
        await OpenAsync(NewReplica());
        var fresh = NewReplica();

        fresh.Registry.GetHealth().ShouldAllBe(h => h.Circuit == CircuitState.Closed); // nothing observed locally yet
        var health = await fresh.Registry.ReadHealthAsync(CancellationToken.None);

        health.Single(h => h.Source == SourceSystem.Sales).Circuit.ShouldBe(CircuitState.Open);
        health.Single(h => h.Source == SourceSystem.Service).Circuit.ShouldBe(CircuitState.Closed);
    }

    private static async Task OpenAsync(Replica replica)
    {
        for (var i = 0; i < 3; i++)
        {
            await replica.CallAsync(HttpStatusCode.InternalServerError);
        }
    }

    internal enum Call
    {
        Passed,
        Rejected,
    }

    /// <summary>One API process: its own registry and pipeline over the shared store.</summary>
    internal sealed class Replica
    {
        private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

        public Replica(ICircuitStateStore store, ResilienceOptions options, TimeProvider time)
        {
            Registry = new SourceCircuitRegistry(store, time);
            var strategy = new SharedCircuitBreakerStrategy(SourceSystem.Sales, store, Registry, options, time);
            _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>().AddStrategy(_ => strategy).Build();
        }

        public SourceCircuitRegistry Registry { get; }

        public int Calls { get; private set; }

        public Task<Call> CallAsync(HttpStatusCode status) =>
            CallAsync(() => Task.FromResult(new HttpResponseMessage(status)));

        public async Task<Call> CallAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _pipeline.ExecuteAsync(
                    async _ =>
                    {
                        Calls++;
                        return await send();
                    },
                    cancellationToken);
                return Call.Passed;
            }
            catch (BrokenCircuitException)
            {
                return Call.Rejected;
            }
            catch (OperationCanceledException)
            {
                return Call.Passed;
            }
        }
    }
}
