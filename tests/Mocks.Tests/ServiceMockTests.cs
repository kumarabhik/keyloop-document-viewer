using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using ServiceSystem.Mock;

namespace Mocks.Tests;

public sealed class ServiceMockTests : IDisposable
{
    private readonly WebApplicationFactory<ServiceMockMarker> _factory = new();
    private readonly HttpClient _client;

    public ServiceMockTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<JsonArray> GetRecordsAsync(string vin)
    {
        var response = await _client.GetAsync(new Uri($"/service/api/records?vin={vin}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["records"]!.AsArray();
    }

    [Theory]
    [InlineData("MA3EWB81S00100001", 5)]
    [InlineData("MA3EWB81S00100003", 4)]
    [InlineData("MA3EWB81S00100008", 30)]
    public async Task KnownVin_ReturnsSnakeCaseRecords(string vin, int expectedCount)
    {
        var records = await GetRecordsAsync(vin);

        records.Count.ShouldBe(expectedCount);
        records[0]!["record_no"].ShouldNotBeNull();
        records[0]!["odometer_km"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Dates_UseDayMonthYearFormat()
    {
        var records = await GetRecordsAsync("MA3EWB81S00100006");

        records.Select(r => r!["created"]!.GetValue<string>()).ShouldContain("11/02/2024");
    }

    [Fact]
    public async Task RelativeDateTokens_AreResolvedToDayMonthYear()
    {
        var records = await GetRecordsAsync("MA3EWB81S00100005");
        var puc = records.Single(r => r!["category"]!.GetValue<string>() == "PUC")!;

        var expected = DateOnly.FromDateTime(DateTime.Now).AddDays(10).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        puc["expiry"]!.GetValue<string>().ShouldBe(expected);
    }

    [Theory]
    [InlineData("MA3EWB81S00100002")] // Sales-only vehicle
    [InlineData("MA3EWB81S00100004")] // No documents anywhere
    public async Task UnknownVin_ReturnsEmptyListNot404(string vin)
    {
        (await GetRecordsAsync(vin)).ShouldBeEmpty();
    }

    [Fact]
    public async Task MissingVin_Returns400()
    {
        var response = await _client.GetAsync(new Uri("/service/api/records", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Vin9_ContainsOneRecordWithImpossibleDate()
    {
        var records = await GetRecordsAsync("MA3EWB81S00100009");

        records.Count(r => r!["created"]!.GetValue<string>() == "31/02/2024").ShouldBe(1);
    }
}
