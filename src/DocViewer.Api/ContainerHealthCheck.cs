namespace DocViewer.Api;

/// <summary>
/// <c>dotnet DocViewer.Api.dll --healthcheck [path]</c>: probes the API already running in this container and
/// exits 0 when it answers 2xx, otherwise 1. Exists because the chiselled runtime image has no shell or curl
/// for a Docker HEALTHCHECK (D-023). Defaults to <c>/readyz</c>.
/// </summary>
internal static class ContainerHealthCheck
{
    public const string Flag = "--healthcheck";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Flag;

    public static Task<int> RunAsync(string[] args)
    {
        var path = args.Length > 1 ? args[1] : "/readyz";
        // ASP.NET images listen on ASPNETCORE_HTTP_PORTS (8080 by default); probe the first one.
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split([';', ','])[0] ?? "8080";
        return ProbeAsync(new Uri($"http://localhost:{port}{path}"), TimeSpan.FromSeconds(3));
    }

    internal static async Task<int> ProbeAsync(Uri uri, TimeSpan timeout)
    {
        // A one-shot process with no DI container, so IHttpClientFactory (AGENTS.md §6) does not apply here.
        using var http = new HttpClient { Timeout = timeout };
        try
        {
            using var response = await http.GetAsync(uri);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1; // not listening yet, refused, or too slow: unhealthy
        }
    }
}
