using System.Net;
using System.Text.Json.Nodes;

namespace DocViewer.Api.Tests;

/// <summary>D-031: opt-in keyset pagination with <c>?limit=</c> and <c>?cursor=</c> (backlog, Q1).</summary>
public sealed class PaginationTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public PaginationTests()
    {
        _client = _factory.CreateClient();
        _factory.SalesReturns(Payloads.Vin, 200, Payloads.Sales);
        _factory.ServiceReturns(Payloads.Vin, 200, Payloads.Service);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<(HttpStatusCode Status, JsonNode Body)> GetAsync(string query)
    {
        var response = await _client.GetAsync(new Uri($"/api/v1/vehicles/{Payloads.Vin}/documents{query}", UriKind.Relative));
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    private static List<string> Ids(JsonNode body) => body["documents"]!.AsArray().Select(d => d!["id"]!.GetValue<string>()).ToList();

    [Fact]
    public async Task NoLimit_IsTheFullListAsBefore_WithNoNextCursor()
    {
        var (status, body) = await GetAsync("");

        status.ShouldBe(HttpStatusCode.OK);
        Ids(body).Count.ShouldBe(4);
        body["nextCursor"].ShouldBeNull();
        body["summary"]!["total"]!.GetValue<int>().ShouldBe(4);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [Trait("Invariant", "8")]
    public async Task WalkingThePages_GivesTheFullListOnce_InOrder(int limit)
    {
        var (_, full) = await GetAsync("");
        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var (status, page) = await GetAsync($"?limit={limit}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"));
            status.ShouldBe(HttpStatusCode.OK);
            Ids(page).Count.ShouldBeLessThanOrEqualTo(limit);
            page["summary"]!["total"]!.GetValue<int>().ShouldBe(4); // the whole result, on every page
            seen.AddRange(Ids(page));
            cursor = page["nextCursor"]?.GetValue<string>();
            pages++;
        }
        while (cursor is not null && pages < 10);

        seen.ShouldBe(Ids(full)); // nothing skipped, nothing repeated, same order
        pages.ShouldBe((int)Math.Ceiling(4.0 / limit));
    }

    [Fact]
    public async Task NewDocumentBetweenPages_IsNotRepeatedOrSkippedOnThePagesAlreadyWalked()
    {
        var (_, first) = await GetAsync("?limit=2");
        var cursor = first["nextCursor"]!.GetValue<string>();

        // Sales now has a brand-new document that sorts first; an offset would shift every later page by one.
        _factory.Sales.Reset();
        const string NewDocument = "{\"docId\":\"NEW-1\",\"docType\":\"SALE_INVOICE\",\"title\":\"New\",\"issuedOn\":\"2026-10-01\",\"validUntil\":null,\"meta\":{}},";
        var withNewDocument = Payloads.Sales.Replace("\"documents\":[", "\"documents\":[" + NewDocument, StringComparison.Ordinal);
        withNewDocument.ShouldNotBe(Payloads.Sales); // the test must really add a document
        _factory.SalesReturns(Payloads.Vin, 200, withNewDocument);
        var (_, second) = await GetAsync($"?limit=2&refresh=true&cursor={Uri.EscapeDataString(cursor)}");

        var firstIds = Ids(first);
        Ids(second).ShouldNotContain(firstIds[0]);
        Ids(second).ShouldNotContain(firstIds[1]);
        Ids(second).ShouldNotContain("SALES:NEW-1"); // it sorts before the cursor, so it belongs to page 1 now
        Ids(second).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Filters_ApplyBeforePaging()
    {
        var (_, page) = await GetAsync("?source=SERVICE&limit=1");

        Ids(page).ShouldHaveSingleItem().ShouldStartWith("SERVICE:");
        page["summary"]!["total"]!.GetValue<int>().ShouldBe(2);
        page["nextCursor"].ShouldNotBeNull();
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?limit=2&cursor=not-a-cursor")]
    [InlineData("?limit=2&cursor=djJ8MjAyNS0wMS0wMXxTYWxlc3xB")] // well-formed apart from its unknown version: "v2|2025-01-01|Sales|A"
    [Trait("Invariant", "6")]
    public async Task BadPageRequest_Is400Problem_AndCallsNoSource(string query)
    {
        var (status, body) = await GetAsync(query);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body["type"]!.GetValue<string>().ShouldEndWith("/invalid-page");
        _factory.Sales.LogEntries.ShouldBeEmpty();
    }
}
