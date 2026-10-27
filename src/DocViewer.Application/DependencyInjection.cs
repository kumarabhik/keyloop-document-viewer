using DocViewer.Application.Documents;
using DocViewer.Application.Observability;
using DocViewer.Application.Options;
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

        services.AddOptions<CacheOptions>()
            .BindConfiguration(CacheOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DealershipCalendar>();

        services.TryAddSingleton<SourceActivity>();
        services.TryAddSingleton<DocViewerMetrics>(); // needs IMeterFactory (the host provides it) and ISourceHealth

        // Singleton: it owns the per-process single-flight map (D-020).
        services.TryAddSingleton<AggregationService>();

        return services;
    }
}
