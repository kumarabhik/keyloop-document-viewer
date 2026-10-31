using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DocViewer.Api.Tests;

/// <summary>
/// D-034 through the real host with auth on: bearer tokens signed by a local test key (no issuer on the network), and
/// documents scoped to the token's dealer_id claims.
/// </summary>
public sealed class DealerAuthTests : IDisposable
{
    private const string Issuer = "https://issuer.test/realms/dealers";
    private const string Audience = "docviewer-api";
    private const string Path = $"/api/v1/vehicles/{Payloads.Vin}/documents";

    // Sales: vehicle sold by NEXA-KOR. Service: invoice copy at NEXA-WFD (same paper as the Sales invoice ⇒ a
    // duplicate across dealers), PUC at NEXA-KOR.
    private const string ScopedSales = """
        {"vehicle":{"vin":"MA3EWB81S00100001","dealerCode":"NEXA-KOR"},"documents":[
          {"docId":"INV-2024-0311","docType":"SALE_INVOICE","title":"Sales Invoice","issuedOn":"2024-02-11","validUntil":null,
           "meta":{"refNo":"BLR/24/0311"}},
          {"docId":"INS-1","docType":"INSURANCE_POLICY","title":"Insurance","issuedOn":"2019-01-01","validUntil":"2020-01-01","meta":{}}
        ]}
        """;

    private const string ScopedService = """
        {"records":[
          {"record_no":"SV-INV-BLR/24/0311","category":"INV","description":"Invoice copy","created":"11/02/2024","expiry":null,
           "workshop":"Whitefield","dealer_code":"NEXA-WFD","odometer_km":0},
          {"record_no":"SV-PUC-1","category":"PUC","description":"PUC certificate","created":"01/03/2026","expiry":"31/08/2099",
           "workshop":"Koramangala","dealer_code":"NEXA-KOR","odometer_km":12000},
          {"record_no":"SV-JC-9","category":"JC","description":"Job card without a dealer","created":"01/04/2026","expiry":null,
           "workshop":"Unknown","odometer_km":13000}
        ]}
        """;

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    private readonly ApiFactory _factory = new();
    private readonly WebApplicationFactory<ApiMarker> _app;
    private readonly HttpClient _client;

    public DealerAuthTests()
    {
        _app = WithAuth(_factory);
        _client = _app.CreateClient();
        _factory.SalesReturns(Payloads.Vin, 200, ScopedSales);
        _factory.ServiceReturns(Payloads.Vin, 200, ScopedService);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        _factory.Dispose();
    }

    private static WebApplicationFactory<ApiMarker> WithAuth(ApiFactory factory) => factory.WithWebHostBuilder(b => b
        .UseSetting("Auth:Enabled", "true")
        .UseSetting("Auth:Authority", Issuer)
        .UseSetting("Auth:Audience", Audience)
        .ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            // The real setup downloads keys from the issuer's discovery document; here the configuration is fixed.
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            configuration.SigningKeys.Add(SigningKey);
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        })));

    private static string Token(
        string[]? dealers = null,
        string[]? roles = null,
        string audience = Audience,
        SecurityKey? key = null,
        DateTime? expires = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = "user-1" };
        if (dealers is not null)
        {
            claims["dealer_id"] = dealers;
        }

        if (roles is not null)
        {
            claims["roles"] = roles;
        }

        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            NotBefore = (expires ?? now.AddMinutes(5)).AddMinutes(-10),
            IssuedAt = (expires ?? now.AddMinutes(5)).AddMinutes(-10),
            Expires = expires ?? now.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private async Task<(HttpResponseMessage Response, JsonNode? Body)> GetAsync(string? token, string path = Path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var json = response.Content.Headers.ContentType?.MediaType?.EndsWith("json", StringComparison.Ordinal) == true;
        return (response, json && text.Length > 0 ? JsonNode.Parse(text) : null); // /healthz and /metrics are plain text
    }

    private int SourceCalls => _factory.Sales.LogEntries.Count + _factory.Service.LogEntries.Count;

    [Fact]
    public async Task NoToken_Is401Problem_AndNoSourceIsCalled()
    {
        var (response, body) = await GetAsync(token: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldStartWith("Bearer");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        body!["traceId"].ShouldNotBeNull();
        SourceCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("other-key")]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    public async Task InvalidToken_Is401(string problem)
    {
        var token = problem switch
        {
            "other-key" => Token(["NEXA-KOR"], key: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" }),
            "wrong-audience" => Token(["NEXA-KOR"], audience: "someone-else"),
            _ => Token(["NEXA-KOR"], expires: DateTime.UtcNow.AddHours(-1)),
        };

        var (response, _) = await GetAsync(token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        SourceCalls.ShouldBe(0);
    }

    [Fact]
    public async Task ValidTokenWithoutDealerClaim_Is403Problem()
    {
        var (response, body) = await GetAsync(Token(dealers: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        body!["status"]!.GetValue<int>().ShouldBe(403);
        SourceCalls.ShouldBe(0);
    }

    [Fact]
    public async Task DealerSeesOnlyItsDocuments_AndMarksDoNotRevealHiddenOnes()
    {
        var (response, body) = await GetAsync(Token(["NEXA-KOR"]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var documents = body!["documents"]!.AsArray();
        documents.Select(d => d!["id"]!.GetValue<string>()).ShouldBe(["SERVICE:SV-PUC-1", "SALES:INV-2024-0311", "SALES:INS-1"], ignoreOrder: true);
        documents.ShouldAllBe(d => d!["dealerCode"]!.GetValue<string>() == "NEXA-KOR");

        // The Sales invoice duplicates NEXA-WFD's copy; neither the flag nor duplicateOf may point at it.
        var invoice = documents.Single(d => d!["id"]!.GetValue<string>() == "SALES:INV-2024-0311")!;
        invoice["flags"]!.AsArray().ShouldBeEmpty();
        invoice["metadata"]!.AsObject().ContainsKey("duplicateOf").ShouldBeFalse();
        body["summary"]!["total"]!.GetValue<int>().ShouldBe(3);
        body["summary"]!["duplicates"]!.GetValue<int>().ShouldBe(0);
        body["sources"]!.AsArray().Single(s => s!["name"]!.GetValue<string>() == "SERVICE")!["count"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public async Task TwoDealers_ShareOneCachedResult_ButEachSeesOnlyItsOwnPart()
    {
        await GetAsync(Token(["NEXA-KOR"]));
        var (response, body) = await GetAsync(Token(["NEXA-WFD"]));

        response.Headers.GetValues("X-Cache").Single().ShouldBe("HIT");
        body!["documents"]!.AsArray().Select(d => d!["id"]!.GetValue<string>()).ShouldBe(["SERVICE:SV-INV-BLR/24/0311"]);
        _factory.Sales.LogEntries.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SeveralDealerClaims_SeeEachDealersDocuments_AndCrossDealerDuplicatesAgain()
    {
        var (_, body) = await GetAsync(Token(["NEXA-KOR", "nexa-wfd"])); // codes compare case-insensitively

        var documents = body!["documents"]!.AsArray();
        documents.Count.ShouldBe(4); // the job card without a dealer stays hidden (fail closed)
        documents.Single(d => d!["id"]!.GetValue<string>() == "SALES:INV-2024-0311")!["flags"]!.AsArray()
            .Select(f => f!.GetValue<string>()).ShouldBe(["POSSIBLE_DUPLICATE"]);
    }

    [Fact]
    public async Task Refresh_NeedsTheAdminRole()
    {
        var (forbidden, problem) = await GetAsync(Token(["NEXA-KOR"]), Path + "?refresh=true");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        problem!["type"]!.GetValue<string>().ShouldEndWith("/forbidden");
        SourceCalls.ShouldBe(0);

        var (allowed, _) = await GetAsync(Token(["NEXA-KOR"], roles: ["admin"]), Path + "?refresh=true");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        allowed.Headers.GetValues("X-Cache").Single().ShouldBe("BYPASS");
    }

    [Fact]
    [Trait("Invariant", "6")]
    public async Task InvalidVin_WithAValidToken_Is400_WithoutSourceCalls()
    {
        var (response, _) = await GetAsync(Token(["NEXA-KOR"]), "/api/v1/vehicles/NOT-A-VIN/documents");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        SourceCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    [InlineData("/metrics")]
    [InlineData("/openapi/v1.json")]
    public async Task OperationalEndpoints_StayAnonymous(string path)
    {
        var (response, _) = await GetAsync(token: null, path);

        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task SourceHealth_NeedsAToken()
    {
        (await GetAsync(token: null, "/api/v1/sources/health")).Response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(Token(["NEXA-KOR"]), "/api/v1/sources/health")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public void EnabledWithoutAuthorityOrAudience_RefusesToStart()
    {
        using var factory = new ApiFactory();
        using var broken = factory.WithWebHostBuilder(b => b.UseSetting("Auth:Enabled", "true"));

        var error = Should.Throw<OptionsValidationException>(() => broken.CreateClient());

        error.Message.ShouldContain("Auth:Authority");
        error.Message.ShouldContain("Auth:Audience");
    }
}
