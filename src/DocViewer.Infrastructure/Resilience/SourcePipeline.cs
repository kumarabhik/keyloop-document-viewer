using System.Net;
using DocViewer.Infrastructure.Options;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace DocViewer.Infrastructure.Resilience;

/// <summary>
/// The per-source resilience pipeline from D-014, outermost first:
/// total timeout → retry (max 1) → circuit breaker → attempt timeout.
/// The breaker keeps D-014's rules but stores its state where replicas can share it (D-032).
/// </summary>
internal static class SourcePipeline
{
    public static void Configure(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        ResilienceOptions options,
        SharedCircuitBreakerStrategy breaker)
    {
        builder
            .AddTimeout(options.TotalTimeout)
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(options.MaxRetryAttempts, 1),
                Delay = options.RetryBaseDelay,
                BackoffType = DelayBackoffType.Constant,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(
                    options.MaxRetryAttempts > 0 && IsTransient(args.Outcome)),
            })
            .AddStrategy(_ => breaker)
            .AddTimeout(options.AttemptTimeout);
    }

    /// <summary>
    /// Retry only failures that a second attempt can plausibly fix. Attempt timeouts and an open
    /// circuit are not retried: that would blow the 2 s budget or hammer a dead source (D-005).
    /// Polly's default would retry timeouts, so this must stay explicit.
    /// </summary>
    internal static bool IsTransient(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException
        || outcome.Result?.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}
