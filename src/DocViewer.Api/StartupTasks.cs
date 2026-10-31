using DocViewer.Application.Documents;
using DocViewer.Application.Persistence;
using DocViewer.Domain;
using DocViewer.Infrastructure;
using DocViewer.Infrastructure.Options;
using Microsoft.Extensions.Options;

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
    /// The set-up is then retried in the background until it succeeds (E-024).
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
            var firstWait = app.Services.GetRequiredService<IOptions<PersistenceOptions>>().Value.SchemaRetry;
            _ = RetryInBackgroundAsync(app, "database", firstWait, ct => app.Services.InitializeDatabaseAsync(ct));
        }

        await WarmUpSnapshotReadAsync(app);
        await WarmUpCacheAsync(app);
        await WarmUpSharedCircuitStateAsync(app);
    }

    /// <summary>
    /// Retries <paramref name="work"/> with a doubling wait (at most 60 s) until it succeeds or the app stops. Found on
    /// Kubernetes, which has no start order: the API pods started before Postgres and Redis, the start-up work failed
    /// once, and nothing ever tried again: no tables for snapshots and audit (E-024), a cold shared breaker (E-026).
    /// </summary>
    private static async Task RetryInBackgroundAsync(WebApplication app, string task, TimeSpan firstWait, Func<CancellationToken, Task> work)
    {
        var stopping = app.Lifetime.ApplicationStopping;
        var wait = firstWait;
        for (var attempt = 2; !stopping.IsCancellationRequested; attempt++)
        {
            try
            {
                await Task.Delay(wait, stopping);
                await work(stopping);
                LogRetrySucceeded(app.Logger, task, attempt);
                return;
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogRetryFailed(app.Logger, task, attempt, ex);
                wait = TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds * 2, 60));
            }
        }
    }

    /// <summary>
    /// Connects to the shared breaker state and runs each of its Redis commands once before the first search (D-032),
    /// at most 5 s per try. If Redis is not up yet, keeps trying in the background: a cold first call missed the 50 ms
    /// budget and sent the pod to local breaker state while the cluster was under test (E-022, E-026).
    /// </summary>
    private static async Task WarmUpSharedCircuitStateAsync(WebApplication app)
    {
        try
        {
            await WarmUpSharedCircuitStateOnceAsync(app, app.Lifetime.ApplicationStopping);
        }
        catch (Exception ex) when (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            LogSharedCircuitWarmUpFailed(app.Logger, ex); // the breaker falls back to local state (invariant 10)
            var firstWait = app.Services.GetRequiredService<IOptions<ResilienceOptions>>().Value.SharedStateRetryAfter;
            _ = RetryInBackgroundAsync(app, "shared_circuit", firstWait, ct => WarmUpSharedCircuitStateOnceAsync(app, ct));
        }
    }

    private static async Task WarmUpSharedCircuitStateOnceAsync(WebApplication app, CancellationToken stopping)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await app.Services.WarmUpSharedCircuitStateAsync(timeout.Token);
    }

    /// <summary>
    /// One throwaway cache read, so a new pod's first search does not open the Redis connection inside its 150 ms cache
    /// budget (a guaranteed miss, E-019; seen again on Kubernetes). Best effort, at most 5 s; the cache never throws.
    /// </summary>
    private static async Task WarmUpCacheAsync(WebApplication app)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await app.Services.GetRequiredService<IDocumentCache>().GetAsync(WarmUpVin, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // Redis is slow or down; searches treat that as a miss (invariant 10).
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

    [LoggerMessage(EventId = 902, Level = LogLevel.Information, Message = "startup.retry_succeeded task={Task} attempt={Attempt}")]
    private static partial void LogRetrySucceeded(ILogger logger, string task, int attempt);

    [LoggerMessage(EventId = 903, Level = LogLevel.Warning, Message = "startup.retry_failed task={Task} attempt={Attempt}")]
    private static partial void LogRetryFailed(ILogger logger, string task, int attempt, Exception exception);

    [LoggerMessage(EventId = 901, Level = LogLevel.Warning, Message = "startup.shared_circuit_warmup_failed; breaker starts on local state")]
    private static partial void LogSharedCircuitWarmUpFailed(ILogger logger, Exception exception);
}

/// <summary>Entry-assembly marker for <c>WebApplicationFactory</c> in tests.</summary>
public sealed class ApiMarker;
