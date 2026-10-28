using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocViewer.Domain;
using Microsoft.Extensions.Options;

namespace DocViewer.Web.Api;

/// <summary>Typed client for the DocViewer API and the mocks' fault switches. Never throws for expected outcomes.</summary>
internal sealed class DocumentsApiClient(HttpClient http, IOptions<ApiOptions> options)
{
    public const string TraceIdHeader = "X-Trace-Id";

    /// <summary>Used when a 503 has no readable <c>Retry-After</c>; matches the API's value (DESIGN.md §7.3).</summary>
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(5);

    public async Task<SearchOutcome> SearchAsync(Vin vin, bool refresh, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vin);
        var path = $"api/v1/vehicles/{vin.Value}/documents{(refresh ? "?refresh=true" : "")}";
        try
        {
            using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
            var traceId = response.Headers.TryGetValues(TraceIdHeader, out var values) ? values.FirstOrDefault() : null;

            return response.StatusCode switch
            {
                HttpStatusCode.OK => await ReadAsync(response, WebJsonContext.Default.DocumentsResponse, cancellationToken) is { } body
                    ? new SearchOutcome.Found(body)
                    : new SearchOutcome.Failed("The API returned an empty response.", traceId),
                HttpStatusCode.BadRequest => new SearchOutcome.Rejected(await ReadProblemAsync(response, traceId, cancellationToken)),
                HttpStatusCode.ServiceUnavailable => new SearchOutcome.Unavailable(
                    await ReadProblemAsync(response, traceId, cancellationToken),
                    response.Headers.RetryAfter?.Delta ?? DefaultRetryAfter),
                _ => new SearchOutcome.Failed($"The API answered HTTP {(int)response.StatusCode}.", traceId),
            };
        }
        catch (HttpRequestException)
        {
            return new SearchOutcome.Failed($"Cannot reach the API at {options.Value.BaseUrl}. Is it running?", null);
        }
        catch (JsonException)
        {
            return new SearchOutcome.Failed("The API returned a response this page could not read.", null);
        }
    }

    /// <summary>Null when the API cannot be reached; the panel shows that instead of failing the page.</summary>
    public async Task<IReadOnlyList<SourceHealthResponse>?> GetSourceHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetFromJsonAsync(new Uri("api/v1/sources/health", UriKind.Relative),
                WebJsonContext.Default.IReadOnlyListSourceHealthResponse, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Flips a mock source system's fault mode (demo only, D5.2). True when the mock accepted it.</summary>
    public async Task<bool> SetFaultAsync(string source, string mode, CancellationToken cancellationToken)
    {
        var baseUrl = source == WireValues.Sales ? options.Value.SalesMockUrl : options.Value.ServiceMockUrl;
        try
        {
            using var response = await http.PostAsJsonAsync(new Uri(baseUrl, "__admin/fault"), new FaultRequest(mode),
                WebJsonContext.Default.FaultRequest, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static async Task<ProblemResponse> ReadProblemAsync(HttpResponseMessage response, string? traceId, CancellationToken cancellationToken) =>
        await ReadAsync(response, WebJsonContext.Default.ProblemResponse, cancellationToken)
        ?? new ProblemResponse(null, response.ReasonPhrase, (int)response.StatusCode, null, null, traceId, null);

    // Problem bodies are application/problem+json, so read the stream rather than rely on the media-type check.
    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return stream.CanSeek && stream.Length == 0 ? default : await JsonSerializer.DeserializeAsync(stream, type, cancellationToken);
    }
}
