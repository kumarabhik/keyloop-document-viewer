using DocViewer.Web.Api;
using DocViewer.Web.Services;
using Microsoft.Extensions.Options;

namespace DocViewer.Web;

internal static class WebServiceCollectionExtensions
{
    /// <summary>Everything the pages need; tests call it too, so they run the same wiring.</summary>
    public static IServiceCollection AddDocViewerWeb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiOptions>().Bind(configuration.GetSection(ApiOptions.SectionName)).ValidateDataAnnotations();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<RecentSearches>();

        var api = services.AddHttpClient<DocumentsApiClient>((sp, http) =>
            http.BaseAddress = sp.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl);
        if (configuration.GetValue<bool>($"{ApiOptions.SectionName}:{nameof(ApiOptions.UseMocks)}"))
        {
            // One handler for the app's lifetime, so fault toggles persist between searches (D4.5).
            services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<ApiOptions>>().Value;
                return new MockApiHandler(sp.GetRequiredService<TimeProvider>(), options, TimeSpan.FromMilliseconds(options.MockLatencyMs));
            });
            api.ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<MockApiHandler>())
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        }

        return services;
    }
}
