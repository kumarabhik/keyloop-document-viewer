using Microsoft.AspNetCore.Http;

namespace Mocks.Common;

/// <summary>Applies the current <see cref="FaultSettings"/> to every request except admin and health endpoints.</summary>
public sealed class FaultInjectionMiddleware(RequestDelegate next, FaultState state)
{
    private const string MalformedBody = """{"documents": [ {"docId": "BROKEN-1", "docType": """;

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExempt(context.Request.Path))
        {
            await next(context);
            return;
        }

        var fault = state.Current;
        if (fault.EffectiveDelay > TimeSpan.Zero)
        {
            await Task.Delay(fault.EffectiveDelay, context.RequestAborted);
        }

        switch (fault.Mode)
        {
            case FaultMode.Error:
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { error = "injected_failure" }, context.RequestAborted);
                return;

#pragma warning disable CA5394 // Random is fine for simulating flakiness; nothing security-related.
            case FaultMode.Flaky when Random.Shared.NextDouble() < fault.EffectiveRate:
#pragma warning restore CA5394
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new { error = "injected_flaky_failure" }, context.RequestAborted);
                return;

            case FaultMode.Malformed:
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(MalformedBody, context.RequestAborted);
                return;

            default:
                await next(context);
                return;
        }
    }

    private static bool IsExempt(PathString path) =>
        path.StartsWithSegments("/__admin", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/healthz", StringComparison.OrdinalIgnoreCase);
}
