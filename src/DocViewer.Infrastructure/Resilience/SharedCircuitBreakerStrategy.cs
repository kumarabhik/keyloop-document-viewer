using DocViewer.Domain;
using DocViewer.Infrastructure.Options;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using CircuitState = DocViewer.Application.Sources.CircuitState;

namespace DocViewer.Infrastructure.Resilience;

/// <summary>
/// Polly's ratio breaker from D-014/D-027, with its state in an <see cref="ICircuitStateStore"/> so that replicas
/// sharing a store share one failure window, one open circuit and one half-open probe (D-032).
/// Same rules as <c>AddCircuitBreaker</c>: it opens when, within the sampling window, at least the minimum throughput
/// was seen and the failure ratio reaches the threshold; after the break one probe may pass; its result closes or
/// reopens the circuit. An open circuit answers with <see cref="BrokenCircuitException"/>, as Polly's does.
/// </summary>
internal sealed class SharedCircuitBreakerStrategy(
    SourceSystem source,
    ICircuitStateStore store,
    SourceCircuitRegistry registry,
    ResilienceOptions options,
    TimeProvider timeProvider) : ResilienceStrategy<HttpResponseMessage>
{
    private readonly CircuitWindow _window = new(options.BreakerSamplingDuration);

    protected override async ValueTask<Outcome<HttpResponseMessage>> ExecuteCore<TState>(
        Func<ResilienceContext, TState, ValueTask<Outcome<HttpResponseMessage>>> callback,
        ResilienceContext context,
        TState state)
    {
        var now = timeProvider.GetUtcNow();
        var openUntil = await store.GetOpenUntilAsync(source);
        var probe = false;
        if (openUntil is { } until)
        {
            if (now < until)
            {
                registry.Observe(source, CircuitState.Open);
                return Rejected(until - now);
            }

            // Half-open: one caller, on any replica, tests the source. The lease outlives the whole source budget.
            probe = await store.TryAcquireProbeAsync(source, options.TotalTimeout + TimeSpan.FromSeconds(1), now);
            if (!probe)
            {
                registry.Observe(source, CircuitState.HalfOpen);
                return Rejected(TimeSpan.Zero);
            }
        }

        var outcome = await callback(context, state);
        if (outcome.Exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
        {
            return outcome; // the caller gave up, which says nothing about the source (an abandoned probe lease expires)
        }

        await RecordAsync(outcome, probe);
        return outcome;
    }

    private async Task RecordAsync(Outcome<HttpResponseMessage> outcome, bool probe)
    {
        var failed = HttpClientResiliencePredicates.IsTransient(outcome); // what Polly's HTTP breaker counts by default
        var now = timeProvider.GetUtcNow();
        if (probe)
        {
            await (failed ? OpenAsync(now) : CloseAsync());
            return;
        }

        var counts = await store.RecordAsync(source, failed, now, _window);
        if (failed && counts.Total >= options.BreakerMinimumThroughput && counts.FailureRatio >= options.BreakerFailureRatio)
        {
            await OpenAsync(now);
        }
    }

    private async Task OpenAsync(DateTimeOffset now)
    {
        await store.OpenAsync(source, now + options.BreakerBreakDuration, now);
        registry.Observe(source, CircuitState.Open);
    }

    private async Task CloseAsync()
    {
        await store.CloseAsync(source);
        registry.Observe(source, CircuitState.Closed);
    }

    private static Outcome<HttpResponseMessage> Rejected(TimeSpan retryAfter) =>
        Outcome.FromException<HttpResponseMessage>(
            new BrokenCircuitException($"The circuit is open; retry after {Math.Ceiling(retryAfter.TotalSeconds)} s."));
}
