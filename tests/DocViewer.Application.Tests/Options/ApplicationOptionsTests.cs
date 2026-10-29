using DocViewer.Application.Options;
using DocViewer.Application.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocViewer.Application.Tests.Options;

public class ApplicationOptionsTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApplication();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Defaults_MatchDesign()
    {
        using var provider = BuildProvider([]);

        var rules = provider.GetRequiredService<IOptions<BusinessRulesOptions>>().Value;
        var cache = provider.GetRequiredService<IOptions<CacheOptions>>().Value;

        rules.ExpiryWarningDays.ShouldBe(30);
        rules.TimeZone.ShouldBe("Asia/Kolkata");
        cache.Ttl.ShouldBe(TimeSpan.FromSeconds(60));
        provider.GetRequiredService<IOptions<LatencyBudgetOptions>>().Value.SnapshotReadTimeout.ShouldBe(TimeSpan.FromMilliseconds(300));
        provider.GetRequiredService<IOptions<LatencyBudgetOptions>>().Value.CacheTimeout.ShouldBe(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public void Values_BindFromConfiguration()
    {
        using var provider = BuildProvider(new()
        {
            ["BusinessRules:ExpiryWarningDays"] = "14",
            ["Cache:TtlSeconds"] = "120",
        });

        provider.GetRequiredService<IOptions<BusinessRulesOptions>>().Value.ExpiryWarningDays.ShouldBe(14);
        provider.GetRequiredService<IOptions<CacheOptions>>().Value.TtlSeconds.ShouldBe(120);
    }

    [Theory]
    [InlineData("BusinessRules:ExpiryWarningDays", "-1")]
    [InlineData("BusinessRules:TimeZone", "Mars/Olympus_Mons")]
    [InlineData("Cache:TtlSeconds", "0")]
    [InlineData("LatencyBudget:SnapshotReadTimeoutMs", "0")]
    [InlineData("LatencyBudget:SnapshotReadTimeoutMs", "5000")] // longer than the whole 2.2 s promise
    [InlineData("LatencyBudget:CacheTimeoutMs", "5000")]
    public void InvalidValues_FailValidation(string key, string value)
    {
        using var provider = BuildProvider(new() { [key] = value });

        Should.Throw<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<BusinessRulesOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<LatencyBudgetOptions>>().Value;
        });
    }

    [Fact]
    public void TimeProviderAndCalendar_AreRegistered()
    {
        using var provider = BuildProvider([]);

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
        provider.GetRequiredService<DealershipCalendar>().ShouldNotBeNull();
    }
}
