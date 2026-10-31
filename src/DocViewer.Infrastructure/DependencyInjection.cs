using DocViewer.Application.Documents;
using DocViewer.Application.Observability;
using DocViewer.Application.Persistence;
using DocViewer.Application.Sources;
using DocViewer.Domain;
using DocViewer.Infrastructure.Caching;
using DocViewer.Infrastructure.Options;
using DocViewer.Infrastructure.Persistence;
using DocViewer.Infrastructure.Resilience;
using DocViewer.Infrastructure.Sources.Sales;
using DocViewer.Infrastructure.Sources.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DocViewer.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Everything the API needs from this layer: sources, cache and persistence.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddSourceAdapters()
            .AddSharedCircuitState(configuration)
            .AddDocumentCache(configuration)
            .AddPersistence(configuration);

    /// <summary>Source adapters with their typed HTTP clients and resilience pipelines.</summary>
    public static IServiceCollection AddSourceAdapters(this IServiceCollection services)
    {
        services.AddOptions<ResilienceOptions>()
            .BindConfiguration(ResilienceOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<InMemoryCircuitStateStore>();
        services.TryAddSingleton<ICircuitStateStore>(sp => sp.GetRequiredService<InMemoryCircuitStateStore>());
        services.TryAddSingleton<SourceCircuitRegistry>();
        services.TryAddSingleton<ISourceHealth>(sp => sp.GetRequiredService<SourceCircuitRegistry>());

        // Adapters count dropped records; make sure the metrics they need exist even without AddApplication.
        services.AddMetrics();
        services.TryAddSingleton<DocViewerMetrics>();

        AddSource<SalesAdapter>(services, SourceSystem.Sales);
        AddSource<ServiceAdapter>(services, SourceSystem.Service);

        return services;
    }

    /// <summary>
    /// Breaker state shared by every replica through Redis when <c>ConnectionStrings:Redis</c> is set, falling back to
    /// this process's own state while Redis is unreachable (D-032). Without Redis the breaker stays per process.
    /// </summary>
    public static IServiceCollection AddSharedCircuitState(this IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            return services;
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<InMemoryCircuitStateStore>();
        services.AddSingleton(_ => new RedisConnection(redis));
        services.AddSingleton<RedisCircuitStateStore>();
        services.RemoveAll<ICircuitStateStore>();
        services.AddSingleton<ICircuitStateStore>(sp => ActivatorUtilities.CreateInstance<FailoverCircuitStateStore>(
            sp, sp.GetRequiredService<RedisCircuitStateStore>()));
        return services;
    }

    /// <summary>
    /// Response cache: Redis when <c>ConnectionStrings:Redis</c> is set (shared across replicas),
    /// otherwise in-process memory (D-010, D-015).
    /// </summary>
    public static IServiceCollection AddDocumentCache(this IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redis;
                o.InstanceName = "docviewer:";
            });
        }

        services.AddMetrics();
        services.TryAddSingleton<DocViewerMetrics>();
        services.TryAddSingleton<IDocumentCache, DistributedDocumentCache>();
        return services;
    }

    /// <summary>Snapshot repository and search audit over EF Core (D-003, D-019).</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PersistenceOptions>()
            .BindConfiguration(PersistenceOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var provider = configuration.GetSection(PersistenceOptions.SectionName).GetValue(nameof(PersistenceOptions.Provider), DatabaseProvider.Sqlite);
        var connectionString = configuration.GetConnectionString("DocViewer") ?? "Data Source=docviewer.db";

        services.AddDbContextFactory<DocViewerDbContext>(o =>
        {
            if (provider == DatabaseProvider.Postgres)
            {
                o.UseNpgsql(connectionString);
            }
            else
            {
                o.UseSqlite(connectionString);
            }
        });

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISnapshotRepository, EfSnapshotRepository>();
        services.TryAddSingleton<ISearchAuditWriter, EfSearchAuditWriter>();
        return services;
    }

    /// <summary>
    /// Creates or upgrades the schema at startup: EF migrations on PostgreSQL; <c>EnsureCreated</c> on SQLite,
    /// because the committed migrations are generated for PostgreSQL (D-019).
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<DocViewerDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Opens the shared breaker's Redis connection and reads one key, so the first source call does not pay for the
    /// connect: that took ~0.6 s in Docker, far over the 50 ms shared-state budget, and sent the replica to local state
    /// for 10 s (D-032). No-op without Redis. Throws if Redis cannot be reached; the caller decides how long to wait.
    /// </summary>
    public static async Task WarmUpSharedCircuitStateAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        if (services.GetService<RedisCircuitStateStore>() is { } redis)
        {
            await redis.GetOpenUntilAsync(SourceSystem.Sales).WaitAsync(cancellationToken);
        }
    }

    private static void AddSource<TAdapter>(IServiceCollection services, SourceSystem source)
        where TAdapter : class, ISourceAdapter
    {
        var name = source.ToString();

        services.AddOptions<SourceEndpointOptions>(name)
            .BindConfiguration($"{SourceEndpointOptions.SectionName}:{name}")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<TAdapter>((sp, client) =>
            {
                var endpoint = sp.GetRequiredService<IOptionsMonitor<SourceEndpointOptions>>().Get(name);
                var resilience = sp.GetRequiredService<IOptions<ResilienceOptions>>().Value;
                client.BaseAddress = new Uri(endpoint.BaseUrl.TrimEnd('/') + "/");
                // Polly owns timeouts; this is only a backstop in case the pipeline is misconfigured.
                client.Timeout = resilience.TotalTimeout + TimeSpan.FromSeconds(1);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var endpoint = sp.GetRequiredService<IOptionsMonitor<SourceEndpointOptions>>().Get(name);
                var resilience = sp.GetRequiredService<IOptions<ResilienceOptions>>().Value;
                return new SocketsHttpHandler
                {
                    ConnectTimeout = resilience.ConnectTimeout,
                    MaxConnectionsPerServer = endpoint.MaxConnectionsPerServer, // bulkhead per source
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                };
            })
            .AddResilienceHandler($"{name.ToLowerInvariant()}-pipeline", (builder, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<ResilienceOptions>>().Value;
                var breaker = new SharedCircuitBreakerStrategy(
                    source,
                    context.ServiceProvider.GetRequiredService<ICircuitStateStore>(),
                    context.ServiceProvider.GetRequiredService<SourceCircuitRegistry>(),
                    options,
                    context.ServiceProvider.GetRequiredService<TimeProvider>());
                SourcePipeline.Configure(builder, options, breaker);
            });

        services.AddTransient<ISourceAdapter>(sp => sp.GetRequiredService<TAdapter>());
    }
}
