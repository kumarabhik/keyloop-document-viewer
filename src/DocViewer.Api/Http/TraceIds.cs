using System.Diagnostics;

namespace DocViewer.Api.Http;

internal static class TraceIds
{
    public const string HeaderName = "X-Trace-Id";

    /// <summary>The W3C trace id (32 hex chars) that also appears in logs and traces; falls back to the request id.</summary>
    public static string Current(HttpContext context) =>
        Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;

    /// <summary>Adds <c>X-Trace-Id</c> to every response so support can quote it, even for errors.</summary>
    public static IApplicationBuilder UseTraceIdHeader(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.Headers[HeaderName] = Current(context);
            return next(context);
        });
}
