using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using DocViewer.Infrastructure.Tests.Persistence.Postgres;

namespace DocViewer.AppHost.Tests;

/// <summary>
/// Starts the whole system through the Aspire AppHost (real Postgres and Redis containers, both mocks, the API) and
/// makes one real search, so the orchestration cannot silently rot. Needs Docker; skipped with a reason otherwise.
/// Uses the launch-profile ports (8080-8082, 5173): stop the compose stack before running it locally.
/// </summary>
public sealed class AppHostTests
{
    private static readonly TimeSpan StartupBudget = TimeSpan.FromMinutes(4); // the first run pulls two images

    [PostgresFact]
    public async Task WholeSystem_StartsHealthy_AndAnswersASearchFromBothSources()
    {
        using var cts = new CancellationTokenSource(StartupBudget);
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.DocViewer_AppHost>(cts.Token);
        await using var app = await appHost.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token); // /readyz via WithHttpHealthCheck

        using var api = app.CreateHttpClient("api");
        var body = await api.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/vehicles/MA3EWB81S00100001/documents", UriKind.Relative), cts.Token);
        var ready = await api.GetStringAsync(new Uri("/readyz", UriKind.Relative), cts.Token);

        body.GetProperty("partial").GetBoolean().ShouldBeFalse();
        body.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("status").GetString()).ShouldBe(["ok", "ok"]);
        body.GetProperty("documents").GetArrayLength().ShouldBe(9); // seed vehicle 1: 4 Sales + 5 Service
        ready.ShouldBe("Healthy"); // the AppHost's Postgres container is reachable, not just the process
    }
}
