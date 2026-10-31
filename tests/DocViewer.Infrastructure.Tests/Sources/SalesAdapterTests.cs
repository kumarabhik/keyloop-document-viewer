using DocViewer.Application.Sources;
using DocViewer.Domain;
using DocViewer.Infrastructure.Sources.Sales;
using DocViewer.Infrastructure.Tests.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace DocViewer.Infrastructure.Tests.Sources;

public sealed class SalesAdapterTests : IDisposable
{
    private static readonly Vin Vin = Vin.Parse("MA3EWB81S00100006");
    private const string Path = "/sales/v1/vehicles/MA3EWB81S00100006/documents";

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly TestMetrics _metrics = new();
    private readonly HttpClient _http;
    private readonly SalesAdapter _adapter;

    public SalesAdapterTests()
    {
        _http = new HttpClient { BaseAddress = new Uri(_server.Url! + "/") };
        _adapter = new SalesAdapter(_http, TimeProvider.System, _metrics.Metrics, NullLogger<SalesAdapter>.Instance);
    }

    public void Dispose()
    {
        _http.Dispose();
        _metrics.Dispose();
        _server.Stop();
        _server.Dispose();
    }

    private void Respond(int status, string body) =>
        _server.Given(Request.Create().WithPath(Path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

    [Fact]
    public async Task MapsSalesPayloadToCanonicalDocuments()
    {
        Respond(200, """
            {"vehicle":{"vin":"MA3EWB81S00100006"},"documents":[
              {"docId":"INV-2024-0311","docType":"SALE_INVOICE","title":"Sales Invoice","issuedOn":"2024-02-11","validUntil":null,
               "meta":{"refNo":"BLR/24/0311","amount":"845000.00","currency":"INR"}},
              {"docId":"INS-1","docType":"INSURANCE_POLICY","title":"Insurance","issuedOn":"2025-01-01","validUntil":"2026-01-01","meta":{}}
            ]}
            """);

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Ok);
        result.Source.ShouldBe(SourceSystem.Sales);
        result.Documents.Count.ShouldBe(2);

        var invoice = result.Documents[0];
        invoice.Id.ShouldBe("SALES:INV-2024-0311");
        invoice.Type.ShouldBe(DocumentType.Invoice);
        invoice.Date.ShouldBe(new DateOnly(2024, 2, 11));
        invoice.ExpiryDate.ShouldBeNull();
        invoice.ReferenceNumber.ShouldBe("BLR/24/0311");
        invoice.Metadata["amount"].ShouldBe("845000.00");
        invoice.Metadata.ShouldNotContainKey("refNo");
        invoice.Vin.ShouldBe(Vin);

        result.Documents[1].Type.ShouldBe(DocumentType.Insurance);
        result.Documents[1].ExpiryDate.ShouldBe(new DateOnly(2026, 1, 1));
    }

    [Theory]
    [InlineData("SALE_INVOICE", DocumentType.Invoice)]
    [InlineData("REG_CERT", DocumentType.Rc)]
    [InlineData("INSURANCE_POLICY", DocumentType.Insurance)]
    [InlineData("WARRANTY_CARD", DocumentType.Warranty)]
    [InlineData("ACCESSORY_INVOICE", DocumentType.Other)]
    public async Task MapsDocumentTypes(string rawType, DocumentType expected)
    {
        Respond(200, $$"""{"documents":[{"docId":"D1","docType":"{{rawType}}","title":"t","issuedOn":"2024-01-01"}]}""");

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Documents.Single().Type.ShouldBe(expected);
    }

    [Fact]
    public async Task NotFound_MeansNoDocuments_NotAFailure()
    {
        Respond(404, """{"error":"vehicle_not_found"}""");

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Ok);
        result.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task MalformedRecords_AreSkippedAndCounted()
    {
        Respond(200, """
            {"documents":[
              {"docId":"OK-1","docType":"REG_CERT","title":"RC","issuedOn":"2024-01-01"},
              {"docId":"BAD-DATE","docType":"REG_CERT","title":"RC","issuedOn":"11/02/2024"},
              {"docId":"BAD-EXPIRY","docType":"REG_CERT","title":"RC","issuedOn":"2024-01-01","validUntil":"soon"},
              {"docType":"REG_CERT","title":"no id","issuedOn":"2024-01-01"}
            ]}
            """);
        using var dropped = _metrics.Collect<long>("docviewer.adapter.records_dropped");

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Ok);
        result.Documents.Select(d => d.SourceId).ShouldBe(["OK-1"]);
        result.DroppedRecords.ShouldBe(3);
        dropped.GetMeasurementSnapshot()
            .Select(m => $"{m.Tags["source"]}/{m.Tags["reason"]}")
            .Order(StringComparer.Ordinal)
            .ShouldBe(["sales/invalid_issued_on", "sales/invalid_valid_until", "sales/missing_id"]);
    }

    [Fact]
    public async Task UnparseableBody_IsInvalidPayloadError()
    {
        Respond(200, """{"documents": [ {"docId": "BROKEN-1", "docType": """);

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Error);
        result.Error.ShouldBe("invalid_payload");
        result.Documents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(500, "http_500")]
    [InlineData(503, "http_503")]
    [InlineData(401, "http_401")]
    public async Task ErrorStatus_IsHttpError(int status, string expectedError)
    {
        Respond(status, """{"error":"boom"}""");

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Error);
        result.Error.ShouldBe(expectedError);
    }

    [Fact]
    public async Task ConnectionRefused_IsConnectionFailedError()
    {
        _server.Stop();

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Error);
        result.Error.ShouldBe("connection_failed");
    }

    [Fact]
    public async Task HttpClientTimeout_IsTimeout()
    {
        _server.Given(Request.Create().WithPath(Path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"documents":[]}""").WithDelay(TimeSpan.FromSeconds(2)));
        using var http = new HttpClient { BaseAddress = new Uri(_server.Url! + "/"), Timeout = TimeSpan.FromMilliseconds(200) };
        var adapter = new SalesAdapter(http, TimeProvider.System, _metrics.Metrics, NullLogger<SalesAdapter>.Instance);

        var result = await adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Timeout);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        _server.Given(Request.Create().WithPath(Path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"documents":[]}""").WithDelay(TimeSpan.FromSeconds(2)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() => _adapter.GetDocumentsAsync(Vin, cts.Token));
    }

    [Fact]
    public async Task Latency_IsMeasured()
    {
        _server.Given(Request.Create().WithPath(Path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"documents":[]}""").WithDelay(TimeSpan.FromMilliseconds(150)));

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Latency.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(140));
    }

    [Fact]
    public async Task VehicleDealerCode_IsTheDealerOfEverySalesDocument()
    {
        Respond(200, """
            {"vehicle":{"vin":"MA3EWB81S00100006","dealerCode":" NEXA-KOR "},"documents":[
              {"docId":"D1","docType":"SALE_INVOICE","title":"t","issuedOn":"2024-01-01"},
              {"docId":"D2","docType":"REG_CERT","title":"t","issuedOn":"2024-01-02"}]}
            """);

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Documents.Select(d => d.DealerCode).ShouldBe(["NEXA-KOR", "NEXA-KOR"]);
    }

    [Fact]
    public async Task MissingVehicleDealerCode_LeavesTheDealerUnknown()
    {
        Respond(200, """{"documents":[{"docId":"D1","docType":"SALE_INVOICE","title":"t","issuedOn":"2024-01-01"}]}""");

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Documents.Single().DealerCode.ShouldBeNull();
    }
}
