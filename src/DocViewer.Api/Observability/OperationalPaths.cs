namespace DocViewer.Api.Observability;

/// <summary>Probe and scrape endpoints: kept out of traces and, when they succeed, out of the access log.</summary>
internal static class OperationalPaths
{
    public static bool Contains(PathString path) =>
        path.StartsWithSegments("/metrics") || path.StartsWithSegments("/healthz") || path.StartsWithSegments("/readyz");
}
