using DocViewer.Application.Documents;
using DocViewer.Application.Observability;
using DocViewer.Application.Options;
using DocViewer.Application.Refresh;
using DocViewer.Application.Sources;
using DocViewer.Application.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocViewer.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<BusinessRulesOptions>()
            .BindConfiguration(BusinessRulesOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LatencyBudgetOptions>()
            .BindConfiguration(LatencyBudgetOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CacheOptions>()
            .BindConfiguration(CacheOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RefreshOptions>()
            .BindConfiguration(RefreshOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DealershipCalendar>();

        services.TryAddSingleton<SourceActivity>();
        services.TryAddSingleton<DocViewerMetrics>(); // needs IMeterFactory (the host provides it) and ISourceHealth

        // Singleton: it owns the per-process single-flight map (D-020).
        services.TryAddSingleton<AggregationService>();

        // D-033: the queue is filled by AggregationService; the host runs BackgroundRefresher.RunAsync.
        services.TryAddSingleton<RefreshScheduler>();
        services.TryAddSingleton<BackgroundRefresher>();

        return services;
    }
}
