using DocViewer.Application.Sources;
using DocViewer.Domain;
using DocViewer.Infrastructure.Sources.Service;
using DocViewer.Infrastructure.Tests.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace DocViewer.Infrastructure.Tests.Sources;

public sealed class ServiceAdapterTests : IDisposable
{
    private static readonly Vin Vin = Vin.Parse("MA3EWB81S00100006");

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly TestMetrics _metrics = new();
    private readonly HttpClient _http;
    private readonly ServiceAdapter _adapter;

    public ServiceAdapterTests()
    {
        _http = new HttpClient { BaseAddress = new Uri(_server.Url! + "/") };
        _adapter = new ServiceAdapter(_http, TimeProvider.System, _metrics.Metrics, NullLogger<ServiceAdapter>.Instance);
    }

    public void Dispose()
    {
        _http.Dispose();
        _metrics.Dispose();
        _server.Stop();
        _server.Dispose();
    }

    private void Respond(int status, string body) =>
        _server.Given(Request.Create().WithPath("/service/api/records").WithParam("vin", Vin.Value).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

    [Fact]
    public async Task MapsServicePayloadToCanonicalDocuments()
    {
        Respond(200, """
            {"records":[
              {"record_no":"SV-INV-BLR/24/0311","category":"INV","description":"Invoice copy","created":"11/02/2024",
               "expiry":null,"workshop":"Koramangala","odometer_km":0},
              {"record_no":"SV-PUC-1","category":"PUC","description":"PUC","created":"01/03/2026","expiry":"31/08/2026",
               "workshop":"Koramangala","odometer_km":12000}
            ]}
            """);

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Ok);
        result.Source.ShouldBe(SourceSystem.Service);

        var invoice = result.Documents[0];
        invoice.Id.ShouldBe("SERVICE:SV-INV-BLR/24/0311");
        invoice.Type.ShouldBe(DocumentType.Invoice);
        invoice.Title.ShouldBe("Invoice copy");
        invoice.Date.ShouldBe(new DateOnly(2024, 2, 11)); // 11 Feb, not 2 Nov
        invoice.ReferenceNumber.ShouldBe("BLR/24/0311");
        invoice.Metadata["workshop"].ShouldBe("Koramangala");

        var puc = result.Documents[1];
        puc.Type.ShouldBe(DocumentType.Puc);
        puc.ExpiryDate.ShouldBe(new DateOnly(2026, 8, 31));
        puc.Metadata["odometerKm"].ShouldBe("12000");
    }

    [Theory]
    [InlineData("INV", DocumentType.Invoice)]
    [InlineData("PUC", DocumentType.Puc)]
    [InlineData("WTY_CLAIM", DocumentType.Warranty)]
    [InlineData("JC", DocumentType.JobCard)]
    [InlineData("SVC", DocumentType.ServiceHistory)]
    [InlineData("TYRE", DocumentType.Other)]
    public async Task MapsCategories(string category, DocumentType expected)
    {
        Respond(200, $$"""{"records":[{"record_no":"SV-{{category}}-1","category":"{{category}}","created":"01/01/2024"}]}""");

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Documents.Single().Type.ShouldBe(expected);
    }

    [Fact]
    public async Task EmptyRecords_MeansNoDocuments()
    {
        Respond(200, """{"records":[]}""");

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Ok);
        result.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task ImpossibleDate_RecordIsDroppedAndCounted()
    {
        Respond(200, """
            {"records":[
              {"record_no":"SV-JC-1","category":"JC","created":"12/12/2023"},
              {"record_no":"SV-JC-2","category":"JC","created":"31/02/2024"},
              {"record_no":"SV-JC-3","category":"JC","created":"2024-02-11"},
              {"record_no":"SV-PUC-4","category":"PUC","created":"01/01/2024","expiry":"never"}
            ]}
            """);

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Ok);
        result.Documents.Select(d => d.SourceId).ShouldBe(["SV-JC-1"]);
        result.DroppedRecords.ShouldBe(3);
    }

    [Fact]
    public async Task NotFound_IsAnErrorForThisSource()
    {
        // Unlike Sales, the Service System never 404s for an unknown VIN, so a 404 means a broken route.
        Respond(404, "{}");

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Error);
        result.Error.ShouldBe("http_404");
    }

    [Fact]
    public async Task UnparseableBody_IsInvalidPayloadError()
    {
        Respond(200, "<html>Gateway error</html>");

        var result = await _adapter.GetDocumentsAsync(Vin, CancellationToken.None);

        result.Status.ShouldBe(SourceStatus.Error);
        result.Error.ShouldBe("invalid_payload");
    }

    [Theory]
    [InlineData("SV-INV-BLR/24/0311", "INV", "BLR/24/0311")]
    [InlineData("SV-WTY_CLAIM-WTY-7007", "WTY_CLAIM", "WTY-7007")]
    [InlineData("sv-jc-88213", "JC", "88213")]
    [InlineData("LEGACY-123", "JC", null)]
    [InlineData("SV-JC-", "JC", null)]
    [InlineData("SV-JC-1", null, null)]
    public void ExtractReference_StripsSourcePrefix(string recordNo, string? category, string? expected)
    {
        ServiceAdapter.ExtractReference(recordNo, category).ShouldBe(expected);
    }
}
