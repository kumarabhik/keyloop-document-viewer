using DocViewer.Application.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DocViewer.Api.Observability;

/// <summary>OpenTelemetry metrics (Prometheus <c>/metrics</c>) and tracing (OTLP) — DESIGN.md §12.2–12.3.</summary>
internal static class TelemetrySetup
{
    public const string ServiceName = "docviewer-api";

    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder)
    {
        // Standard OTel variable; when unset (local runs, tests) traces are collected but not exported.
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(ServiceName))
            .WithMetrics(metrics => metrics
                .AddMeter(DocViewerMetrics.MeterName)
                .AddMeter("Polly") // resilience_polly_strategy_events_total: retries, timeouts, breaker transitions
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter())
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(DocViewerTracing.SourceName)
                    .AddAspNetCoreInstrumentation(o => o.Filter = context => !IsOperationalPath(context.Request.Path))
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddProcessor(new VinRedactionProcessor()); // before any exporter, so nothing leaves unmasked

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter();
                }
            });

        return builder;
    }

    /// <summary>Scrapes and probes would otherwise dominate the trace view.</summary>
    private static bool IsOperationalPath(PathString path) =>
        path.StartsWithSegments("/metrics") || path.StartsWithSegments("/healthz") || path.StartsWithSegments("/readyz");
}
