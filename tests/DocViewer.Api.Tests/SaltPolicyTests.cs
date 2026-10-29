using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace DocViewer.Api.Tests;

/// <summary>R6/D-029: a public demo salt makes audit hashes guessable, so Production refuses to start with one.</summary>
public sealed class SaltPolicyTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient Start(string environment, string salt) =>
        _factory.WithWebHostBuilder(b => b.UseEnvironment(environment).UseSetting("Persistence:VinHashSalt", salt)).CreateClient();

    [Theory]
    [InlineData("local-demo-salt-change-me")]        // docker-compose.yml default
    [InlineData("change-me-to-a-long-random-value")] // .env.example
    [InlineData("local-development-salt")]           // appsettings.Development.json
    [InlineData("please-CHANGE-ME-later")]
    public void Production_WithAPublicDemoSalt_RefusesToStart(string salt)
    {
        var error = Should.Throw<OptionsValidationException>(() => Start("Production", salt));

        error.Message.ShouldContain("VIN_HASH_SALT");
    }

    [Fact]
    public async Task Production_WithARealSalt_Starts()
    {
        using var client = Start("Production", "f3a9c1e07b5d4e62a8c4");

        (await client.GetAsync(new Uri("/healthz", UriKind.Relative))).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Demo")]
    [InlineData("Development")]
    public async Task OtherEnvironments_AcceptTheDemoSalt(string environment)
    {
        using var client = Start(environment, "local-demo-salt-change-me");

        (await client.GetAsync(new Uri("/healthz", UriKind.Relative))).IsSuccessStatusCode.ShouldBeTrue();
    }
}
