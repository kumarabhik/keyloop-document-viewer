using DocViewer.Application.Persistence;
using DocViewer.Domain;
using DocViewer.Infrastructure;

namespace DocViewer.Api;

internal static partial class StartupTasks
{
    /// <summary>
    /// True while <c>dotnet build</c> runs the app to write <c>docs/openapi.json</c>
    /// (Microsoft.Extensions.ApiDescription.Server). The app must start without real configuration or I/O then.
    /// </summary>
    public static bool IsGeneratingOpenApiDocument =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    /// <summary>Placeholder values so options validation passes during build-time document generation only.</summary>
    public static void UseBuildTimePlaceholders(this ConfigurationManager configuration)
    {
        if (IsGeneratingOpenApiDocument)
        {
            configuration.AddInMemoryCollection([new("Persistence:VinHashSalt", "openapi-build-time-only")]);
        }
    }

    /// <summary>
    /// Creates or migrates the schema. A database that is down at startup must not stop the API:
    /// searches still work without snapshots and audit (invariant 10), and <c>/readyz</c> reports it.
    /// </summary>
    public static async Task InitializeDatabaseAsync(WebApplication app)
    {
        if (IsGeneratingOpenApiDocument)
        {
            return;
        }

        try
        {
            await app.Services.InitializeDatabaseAsync(app.Lifetime.ApplicationStopping);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDatabaseInitFailed(app.Logger, ex);
        }

        await WarmUpSnapshotReadAsync(app);
        await WarmUpSharedCircuitStateAsync(app);
    }

    /// <summary>Connects to the shared breaker state before the first search (D-032). Best effort, at most 5 s.</summary>
    private static async Task WarmUpSharedCircuitStateAsync(WebApplication app)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await app.Services.WarmUpSharedCircuitStateAsync(timeout.Token);
        }
        catch (Exception ex) when (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            LogSharedCircuitWarmUpFailed(app.Logger, ex); // the breaker falls back to local state (invariant 10)
        }
    }

    /// <summary>
    /// One throwaway snapshot lookup, so the first real fallback does not pay for EF query compilation and a cold
    /// connection: that cold read took over the 300 ms snapshot budget (E-018) in 2 of 10 test runs. Best effort:
    /// the repository turns failures into "no snapshot", and a dead database cannot hold start-up for more than 5 s.
    /// </summary>
    private static async Task WarmUpSnapshotReadAsync(WebApplication app)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var snapshots = app.Services.GetRequiredService<ISnapshotRepository>();
        try
        {
            await snapshots.GetAsync(WarmUpVin, SourceSystem.Sales, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // The database is slow or down; searches handle that themselves (invariant 10).
        }
    }

    /// <summary>A well-formed VIN no dealer system will hold (all zeros).</summary>
    private static readonly Vin WarmUpVin = Vin.Parse("00000000000000000");

    [LoggerMessage(EventId = 900, Level = LogLevel.Error, Message = "startup.database_init_failed; continuing without persistence")]
    private static partial void LogDatabaseInitFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 901, Level = LogLevel.Warning, Message = "startup.shared_circuit_warmup_failed; breaker starts on local state")]
    private static partial void LogSharedCircuitWarmUpFailed(ILogger logger, Exception exception);
}

/// <summary>Entry-assembly marker for <c>WebApplicationFactory</c> in tests.</summary>
public sealed class ApiMarker;
