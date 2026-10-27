using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using SalesSystem.Mock;

namespace Mocks.Tests;

public sealed class SalesMockTests : IDisposable
{
    private readonly WebApplicationFactory<SalesMockMarker> _factory = new();
    private readonly HttpClient _client;

    public SalesMockTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<JsonNode> GetVehicleAsync(string vin)
    {
        var response = await _client.GetAsync(new Uri($"/sales/v1/vehicles/{vin}/documents", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    [Theory]
    [InlineData("MA3EWB81S00100001", 4)]
    [InlineData("MA3EWB81S00100002", 3)]
    [InlineData("MA3EWB81S00100008", 15)]
    public async Task KnownVin_ReturnsCamelCaseDocuments(string vin, int expectedCount)
    {
        var body = await GetVehicleAsync(vin);

        body["vehicle"]!["vin"]!.GetValue<string>().ShouldBe(vin);
        var documents = body["documents"]!.AsArray();
        documents.Count.ShouldBe(expectedCount);
        documents[0]!["docId"].ShouldNotBeNull();
        documents[0]!["docType"].ShouldNotBeNull();
        documents[0]!["issuedOn"].ShouldNotBeNull();
    }

    [Fact]
    public async Task VinLookup_IsCaseInsensitive()
    {
        var body = await GetVehicleAsync("ma3ewb81s00100001");

        body["documents"]!.AsArray().Count.ShouldBe(4);
    }

    [Theory]
    [InlineData("MA3EWB81S00100003")] // Service-only vehicle
    [InlineData("MA3EWB81S00100004")] // No documents anywhere
    [InlineData("MA3EWB81S00199999")] // Never seen
    public async Task UnknownVin_Returns404(string vin)
    {
        var response = await _client.GetAsync(new Uri($"/sales/v1/vehicles/{vin}/documents", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RelativeDateTokens_AreResolvedToIsoDates()
    {
        var body = await GetVehicleAsync("MA3EWB81S00100005");
        var insurance = body["documents"]!.AsArray().Single(d => d!["docType"]!.GetValue<string>() == "INSURANCE_POLICY")!;

        var expected = DateOnly.FromDateTime(DateTime.Now).AddDays(-20).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        insurance["validUntil"]!.GetValue<string>().ShouldBe(expected);
    }
}
