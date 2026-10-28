using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace DocViewer.Api.Observability;

/// <summary>Serilog, compact JSON to stdout (DESIGN.md §12.1).</summary>
internal static class LoggingSetup
{
    public static WebApplicationBuilder AddStructuredLogging(this WebApplicationBuilder builder)
    {
        // Levels and overrides come from the "Serilog" config section. "System.Net.Http.HttpClient" must stay
        // at Warning or above: at Information it logs outgoing request URLs, which contain the VIN (invariant 7).
        // ReadFrom.Services lets tests plug in a capturing sink. TraceId/SpanId are captured from
        // Activity.Current by Serilog itself and written as @tr/@sp.
        // preserveStaticLogger: keep the logger per host instead of replacing the global Log.Logger, so
        // parallel test hosts cannot write into each other's sinks (E-006).
        builder.Services.AddSerilog(
            (services, logger) => logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.With<DropRequestPathEnricher>()
                .WriteTo.Console(new RenderedCompactJsonFormatter()),
            preserveStaticLogger: true);
        return builder;
    }

    /// <summary>
    /// One access-log line per request. Logs the route template, never the raw path: the path carries
    /// the VIN, and the default request-logging properties would put it in every line (invariant 7).
    /// </summary>
    public static IApplicationBuilder UseAccessLog(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(o =>
        {
            // Without this the middleware falls back to the static Log.Logger (see AddStructuredLogging).
            o.Logger = app.ApplicationServices.GetRequiredService<Serilog.ILogger>();
            o.MessageTemplate = "http.request {RequestMethod} {Route} responded {StatusCode} in {Elapsed:0.0} ms";
            // The container probe calls /readyz every 10 s; successful probes and scrapes are noise (D5.6 review).
            o.GetLevel = (context, _, ex) => ex is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
                : OperationalPaths.Contains(context.Request.Path) ? LogEventLevel.Verbose
                : LogEventLevel.Information;
            o.GetMessageTemplateProperties = (context, _, elapsedMs, statusCode) =>
            [
                new LogEventProperty("RequestMethod", new ScalarValue(context.Request.Method)),
                new LogEventProperty("Route", new ScalarValue(RouteOf(context))),
                new LogEventProperty("StatusCode", new ScalarValue(statusCode)),
                new LogEventProperty("Elapsed", new ScalarValue(elapsedMs)),
            ];
        });

    private static string RouteOf(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";
}
