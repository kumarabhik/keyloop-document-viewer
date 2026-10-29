using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DocViewer.Api.Http;

/// <summary>
/// A fixed one-minute window per client IP on the documents endpoint (R5). Rejected searches never reach a source.
/// Behind a reverse proxy every client shares the proxy's address until forwarded headers are configured.
/// </summary>
internal static class SearchRateLimiting
{
    public const string PolicyName = "search";

    public static IServiceCollection AddSearchRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>()
            .BindConfiguration(RateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services.AddRateLimiter(o =>
        {
            o.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value.PermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0, // answer at once; a queued search would break the latency promise (invariant 5)
                }));

            o.OnRejected = (rejected, cancellationToken) =>
            {
                var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
                rejected.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return new ValueTask(Problems.TooManyRequests(rejected.HttpContext).ExecuteAsync(rejected.HttpContext));
            };
        });
    }
}
