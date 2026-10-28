using System.Net;
using System.Text;
using DocViewer.Domain;
using DocViewer.Web.Api;
using Microsoft.Extensions.Options;

namespace DocViewer.Web.Tests.Api;

public sealed class DocumentsApiClientTests
{
    private static readonly Vin Vin = Vin.Parse("MA3EWB81S00100001");

    private static DocumentsApiClient ClientFor(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }, Options.Create(new ApiOptions { BaseUrl = new Uri("http://api.test/") }));

    [Fact]
    public async Task Search_400_ReturnsRejectedWithTheApiDetail()
    {
        var client = ClientFor(new StubHandler(HttpStatusCode.BadRequest,
            """{"title":"Invalid VIN","status":400,"detail":"VIN cannot contain 'O'.","traceId":"abc"}""", "application/problem+json"));

        var outcome = await client.SearchAsync(Vin, refresh: false, CancellationToken.None);

        var rejected = outcome.ShouldBeOfType<SearchOutcome.Rejected>();
        rejected.Problem.Detail.ShouldBe("VIN cannot contain 'O'.");
        rejected.Problem.TraceId.ShouldBe("abc");
    }

    [Fact]
    public async Task Search_503_ReturnsUnavailableWithRetryAfterAndFailedSources()
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable,
            """{"title":"Document sources unavailable","status":503,"sources":[{"name":"SALES","status":"error","error":"http_500"},{"name":"SERVICE","status":"timeout","error":"timeout"}]}""",
            "application/problem+json", retryAfterSeconds: 7);

        var outcome = await ClientFor(handler).SearchAsync(Vin, refresh: false, CancellationToken.None);

        var unavailable = outcome.ShouldBeOfType<SearchOutcome.Unavailable>();
        unavailable.RetryAfter.ShouldBe(TimeSpan.FromSeconds(7));
        unavailable.Problem.Sources!.Select(s => s.Status).ShouldBe(["error", "timeout"]);
    }

    [Fact]
    public async Task Search_Refresh_AddsTheQueryParameter()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "", "text/plain");

        await ClientFor(handler).SearchAsync(Vin, refresh: true, CancellationToken.None);

        handler.LastUri!.PathAndQuery.ShouldBe("/api/v1/vehicles/MA3EWB81S00100001/documents?refresh=true");
    }

    [Fact]
    public async Task Search_UnexpectedStatus_ReturnsFailedWithTheTraceId()
    {
        var outcome = await ClientFor(new StubHandler(HttpStatusCode.InternalServerError, "", "text/plain", traceId: "t-1"))
            .SearchAsync(Vin, refresh: false, CancellationToken.None);

        var failed = outcome.ShouldBeOfType<SearchOutcome.Failed>();
        failed.Message.ShouldContain("HTTP 500");
        failed.TraceId.ShouldBe("t-1");
    }

    [Fact]
    public async Task Search_ApiUnreachable_ReturnsFailedNamingTheApi()
    {
        var outcome = await ClientFor(new ThrowingHandler()).SearchAsync(Vin, refresh: false, CancellationToken.None);

        outcome.ShouldBeOfType<SearchOutcome.Failed>().Message.ShouldContain("http://api.test/");
    }

    [Fact]
    public async Task Search_MalformedBody_ReturnsFailedInsteadOfThrowing()
    {
        var outcome = await ClientFor(new StubHandler(HttpStatusCode.OK, "{not json", "application/json"))
            .SearchAsync(Vin, refresh: false, CancellationToken.None);

        outcome.ShouldBeOfType<SearchOutcome.Failed>();
    }

    [Fact]
    public async Task Search_CallerCancels_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            ClientFor(new StubHandler(HttpStatusCode.OK, "{}", "application/json")).SearchAsync(Vin, refresh: false, cts.Token));
    }

    [Fact]
    public async Task SourceHealth_ApiUnreachable_ReturnsNull()
    {
        (await ClientFor(new ThrowingHandler()).GetSourceHealthAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task SetFault_PostsToTheChosenMock()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}", "application/json");

        (await ClientFor(handler).SetFaultAsync(WireValues.Service, "timeout", CancellationToken.None)).ShouldBeTrue();

        handler.LastUri.ShouldBe(new Uri("http://localhost:8082/__admin/fault"));
        handler.LastBody.ShouldBe("""{"mode":"timeout"}""");
    }

    private sealed class StubHandler(HttpStatusCode status, string body, string mediaType, int? retryAfterSeconds = null, string? traceId = null)
        : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
            if (retryAfterSeconds is { } seconds)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            if (traceId is not null)
            {
                response.Headers.Add(DocumentsApiClient.TraceIdHeader, traceId);
            }

            return response;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused");
    }
}
