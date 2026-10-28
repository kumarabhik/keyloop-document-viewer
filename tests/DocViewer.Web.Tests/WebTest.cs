using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DocViewer.Web.Tests;

/// <summary>A bUnit context with the app's real service wiring, mock mode without latency, and a fixed clock.</summary>
internal static class WebTest
{
    /// <summary>10:00 on 28 Oct 2026 (UTC, which is also the fake clock's local zone).</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly Today = new(2026, 10, 28);

    public static BunitContext Create(Action<IServiceCollection>? configure = null)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose; // localStorage calls return defaults: an empty history
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "http://api.test",
                ["Api:UseMocks"] = "true",
                ["Api:MockLatencyMs"] = "0",
            })
            .Build();
        ctx.Services.AddDocViewerWeb(config);
        ctx.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        configure?.Invoke(ctx.Services);
        return ctx;
    }

    public static void NavigateTo(this BunitContext ctx, string uri) =>
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo(uri);

    public static string CurrentUri(this BunitContext ctx) =>
        ctx.Services.GetRequiredService<NavigationManager>().Uri;
}
