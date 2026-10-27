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
    }

    [LoggerMessage(EventId = 900, Level = LogLevel.Error, Message = "startup.database_init_failed; continuing without persistence")]
    private static partial void LogDatabaseInitFailed(ILogger logger, Exception exception);
}

/// <summary>Entry-assembly marker for <c>WebApplicationFactory</c> in tests.</summary>
public sealed class ApiMarker;
