using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Serilog.Core;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace DocViewer.Api.Tests;

/// <summary>
/// The real API in memory, with both source systems faked by WireMock servers over real HTTP
/// and a throwaway SQLite file. One instance per test keeps cache and breaker state isolated.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<ApiMarker>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"docviewer-api-test-{Guid.NewGuid():N}.db");

    public WireMockServer Sales { get; } = WireMockServer.Start();

    public WireMockServer Service { get; } = WireMockServer.Start();

    public CollectingSink Logs { get; } = new();

    /// <summary>Spans as they would be exported, i.e. after the VIN redaction processor.</summary>
    public SpanCollection Spans { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILogEventSink>(Logs);
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(Spans));
        });
        builder.UseSetting("Sources:Sales:BaseUrl", Sales.Url);
        builder.UseSetting("Sources:Service:BaseUrl", Service.Url);
        builder.UseSetting("ConnectionStrings:DocViewer", $"Data Source={_databasePath}");
        builder.UseSetting("ConnectionStrings:Redis", "");
        builder.UseSetting("Persistence:VinHashSalt", "api-test-salt");

        // These tests check the HTTP contract, not timing (that is SourcePipelineTests' job). A cold first
        // request (JIT, connection setup) can exceed the 1.5 s production attempt budget and turn into a
        // false 503 — seen once on the first run after a build (E-005).
        builder.UseSetting("Resilience:AttemptTimeoutSeconds", "10");
        builder.UseSetting("Resilience:TotalTimeoutSeconds", "20");

        // Background refresh (D-033) would call the WireMock sources again a few seconds after a partial result and
        // change the request counts these tests assert. Its own test lowers the delay (BackgroundRefreshApiTests).
        builder.UseSetting("Refresh:FirstDelaySeconds", "600");
    }

    public void SalesReturns(string vin, int status, string body) =>
        Sales.Given(Request.Create().WithPath($"/sales/v1/vehicles/{vin}/documents").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

    public void ServiceReturns(string vin, int status, string body) =>
        Service.Given(Request.Create().WithPath("/service/api/records").WithParam("vin", vin).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Sales.Dispose();
            Service.Dispose();
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException)
            {
                // A fire-and-forget audit write may still hold the file; it is in %TEMP%, so leave it.
            }
        }
    }
}
