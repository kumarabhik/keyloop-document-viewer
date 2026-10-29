using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DocViewer.Domain;

namespace DocViewer.Web.Api;

/// <summary>
/// Mock mode (D4.5, D-025): answers the API's and the mocks' routes in-process, so the UI runs with no backend.
/// Each demo VIN shows one state (see <see cref="Vins"/>). The health panel's fault toggles change what later
/// searches return, as the real <c>/__admin/fault</c> switches do.
/// </summary>
internal sealed class MockApiHandler(TimeProvider timeProvider, ApiOptions options, TimeSpan latency) : HttpMessageHandler
{
    private const string Healthy = "none";

    /// <summary>The API's cache TTL (D-006): a repeat search inside it answers <c>HIT</c>, like the real API.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>Demo VINs. 1, 4, 5, 6 and 7 mirror the real seed data (DESIGN.md Appendix A); 11–14 exist only here.</summary>
    public static class Vins
    {
        public const string Complete = "MA3EWB81S00100001";
        public const string Empty = "MA3EWB81S00100004";
        public const string Expiring = "MA3EWB81S00100005";
        public const string Duplicate = "MA3EWB81S00100006";
        public const string Conflict = "MA3EWB81S00100007";
        public const string Partial = "MA3EWB81S00100011";
        public const string Stale = "MA3EWB81S00100012";
        public const string Unavailable = "MA3EWB81S00100013";
        public const string CircuitOpen = "MA3EWB81S00100014";
    }

    private readonly Dictionary<string, string> _faults = new() { [WireValues.Sales] = Healthy, [WireValues.Service] = Healthy };
    private readonly Dictionary<string, string> _lastStatus = [];
    private readonly HashSet<string> _halfOpen = [];
    private readonly Dictionary<string, DateTimeOffset> _cachedAt = [];
    private readonly Lock _gate = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (latency > TimeSpan.Zero)
        {
            await Task.Delay(latency, timeProvider, cancellationToken); // long enough to see the loading state
        }

        var uri = request.RequestUri!; // HttpClient resolves an absolute URI before any handler runs
        if (uri.AbsolutePath == "/__admin/fault")
        {
            return request.Method == HttpMethod.Post
                ? await SetFaultAsync(request, uri, cancellationToken)
                : Json(HttpStatusCode.OK, new FaultRequest(FaultOf(SourceAt(uri))), WebJsonContext.Default.FaultRequest);
        }

        return uri.AbsolutePath.Trim('/').Split('/') switch
        {
            ["api", "v1", "vehicles", var vin, "documents"] => Search(vin, uri.Query.Contains("refresh=true", StringComparison.Ordinal)),
            ["api", "v1", "sources", "health"] => Json<IReadOnlyList<SourceHealthResponse>>(HttpStatusCode.OK, Health(), WebJsonContext.Default.IReadOnlyListSourceHealthResponse),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private HttpResponseMessage Search(string rawVin, bool refresh)
    {
        var traceId = Guid.NewGuid().ToString("N");
        if (!Vin.TryParse(rawVin, out var vin, out var error))
        {
            return Problem(HttpStatusCode.BadRequest, "Invalid VIN", error, traceId, null);
        }

        var sales = SourceFor(WireValues.Sales, vin.Value);
        var service = SourceFor(WireValues.Service, vin.Value);
        lock (_gate)
        {
            _lastStatus[WireValues.Sales] = sales.Status;
            _lastStatus[WireValues.Service] = service.Status;
            _halfOpen.Clear(); // the trial call after a fault has been made
        }

        if (!HasDocuments(sales) && !HasDocuments(service))
        {
            SourceFailure[] failures = [new(sales.Name, sales.Status, sales.Error), new(service.Name, service.Status, service.Error)];
            var problem = Problem(HttpStatusCode.ServiceUnavailable, "Document sources unavailable",
                "No document source answered and no recent snapshot is available. Try again shortly.", traceId, failures);
            problem.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return problem;
        }

        var today = Components.Format.Today(timeProvider, options.DealershipZone); // the server's "today", not the browser's
        var documents = DocumentsFor(vin.Value, today)
            .Where(d => HasDocuments(d.Source == WireValues.Sales ? sales : service))
            .OrderByDescending(d => d.Date).ThenBy(d => d.Source, StringComparer.Ordinal).ThenBy(d => d.SourceId, StringComparer.Ordinal)
            .ToList();
        SourceStatusResponse[] sources =
        [
            sales with { Count = documents.Count(d => d.Source == WireValues.Sales) },
            service with { Count = documents.Count(d => d.Source == WireValues.Service) },
        ];
        var summary = new DocumentSummary(
            documents.Count,
            documents.Count(d => d.Status == WireValues.Expired),
            documents.Count(d => d.Status == WireValues.ExpiringSoon),
            documents.Count(d => d.Flags.Contains(WireValues.PossibleDuplicate)),
            documents.Count(d => d.Flags.Contains(WireValues.Conflict)));
        var body = new DocumentsResponse(vin.Value, sources.Any(s => !s.IsOk), timeProvider.GetUtcNow(),
            CacheStatus(vin.Value, refresh, complete: sources.All(s => s.IsOk)), traceId, sources, summary, documents, NextCursor: null);

        var response = Json(HttpStatusCode.OK, body, WebJsonContext.Default.DocumentsResponse);
        response.Headers.Add(DocumentsApiClient.TraceIdHeader, traceId);
        return response;
    }

    private static bool HasDocuments(SourceStatusResponse source) => source.IsOk || source.IsStale;

    // Like the API: only complete results are cached (D-006); refresh=true skips the read but still stores.
    private string CacheStatus(string vin, bool refresh, bool complete)
    {
        var now = timeProvider.GetUtcNow();
        lock (_gate)
        {
            var hit = !refresh && _cachedAt.TryGetValue(vin, out var at) && now - at < CacheTtl;
            if (!hit && complete)
            {
                _cachedAt[vin] = now;
            }

            return hit ? "HIT" : refresh ? "BYPASS" : "MISS";
        }
    }

    private string FaultOf(string source)
    {
        lock (_gate)
        {
            return _faults[source];
        }
    }

    private string SourceAt(Uri uri) => uri.Authority == options.SalesMockUrl.Authority ? WireValues.Sales : WireValues.Service;

    private SourceStatusResponse SourceFor(string source, string vin)
    {
        var fault = FaultOf(source);
        var outcome = (source, vin) switch
        {
            (WireValues.Service, Vins.Partial) => "timeout",
            (WireValues.Service, Vins.CircuitOpen) => "circuit_open",
            (WireValues.Service, Vins.Stale) => WireValues.Stale,
            (_, Vins.Unavailable) => "error",
            _ => fault switch { Healthy => WireValues.Ok, "timeout" or "slow" => "timeout", _ => "error" },
        };

        return outcome switch
        {
            WireValues.Ok => new SourceStatusResponse(source, WireValues.Ok, 120, 0, null, null),
            WireValues.Stale => new SourceStatusResponse(source, WireValues.Stale, 2001, 0, timeProvider.GetUtcNow().AddHours(-3), "timeout"),
            "timeout" => new SourceStatusResponse(source, "timeout", 2001, 0, null, "timeout"),
            "circuit_open" => new SourceStatusResponse(source, "circuit_open", 0, 0, null, "circuit_open"),
            _ => new SourceStatusResponse(source, "error", 35, 0, null, "http_500"),
        };
    }

    private List<SourceHealthResponse> Health()
    {
        lock (_gate)
        {
            return WireValues.SourceNames.Select(name => new SourceHealthResponse(
                name,
                _faults[name] != Healthy ? "open" : _halfOpen.Contains(name) ? "half_open" : "closed",
                _lastStatus.GetValueOrDefault(name),
                _lastStatus.ContainsKey(name) ? 120 : null,
                _lastStatus.ContainsKey(name) ? timeProvider.GetUtcNow() : null)).ToList();
        }
    }

    private async Task<HttpResponseMessage> SetFaultAsync(HttpRequestMessage request, Uri uri, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await JsonSerializer.DeserializeAsync(await request.Content.ReadAsStreamAsync(cancellationToken), WebJsonContext.Default.FaultRequest, cancellationToken);
        var source = SourceAt(uri);
        var mode = body?.Mode ?? Healthy;
        lock (_gate)
        {
            if (mode == Healthy && _faults[source] != Healthy)
            {
                _halfOpen.Add(source); // healed: like the real breaker, it lets one trial call through first
            }

            _faults[source] = mode;
        }

        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private static IEnumerable<DocumentResponse> DocumentsFor(string vin, DateOnly today)
    {
        DocumentResponse Doc(string source, string id, string type, string title, DateOnly date, DateOnly? expiry, params string[] flags)
        {
            var status = expiry is not { } e ? WireValues.NotApplicable
                : e < today ? WireValues.Expired
                : e <= today.AddDays(30) ? WireValues.ExpiringSoon
                : WireValues.Valid;
            var metadata = new Dictionary<string, string> { ["dealer"] = source == WireValues.Sales ? "Nexa Koramangala" : "Koramangala workshop" };
            return new DocumentResponse($"{source}:{id}", id, vin, title, type, date, expiry, source, status, flags, metadata);
        }

        switch (vin)
        {
            case Vins.Expiring:
                yield return Doc(WireValues.Sales, "INS-5", "INSURANCE", "Comprehensive Insurance", today.AddYears(-1).AddDays(-21), today.AddDays(-21));
                yield return Doc(WireValues.Service, "PUC-5", "PUC", "Pollution Under Control certificate", today.AddMonths(-6).AddDays(10), today.AddDays(10));
                yield return Doc(WireValues.Sales, "RC-5", "RC", "Registration Certificate", today.AddYears(-4), today.AddYears(11));
                yield return Doc(WireValues.Service, "JC-5", "JOB_CARD", "Brake pad replacement", today.AddDays(-40), null);
                break;
            case Vins.Duplicate:
                yield return Doc(WireValues.Sales, "INV-6", "INVOICE", "Sales Invoice", today.AddYears(-2), null, WireValues.PossibleDuplicate);
                yield return Doc(WireValues.Service, "SV-INV-6", "INVOICE", "Accessories invoice copy", today.AddYears(-2), null, WireValues.PossibleDuplicate);
                yield return Doc(WireValues.Service, "JC-6", "JOB_CARD", "First free service", today.AddYears(-2).AddMonths(3), null);
                break;
            case Vins.Conflict:
                yield return Doc(WireValues.Sales, "WAR-7", "WARRANTY", "Standard Warranty", today.AddYears(-2), today.AddYears(1), WireValues.Conflict);
                yield return Doc(WireValues.Service, "SV-WAR-7", "WARRANTY", "Warranty registration", today.AddYears(-2), today.AddYears(2), WireValues.Conflict);
                yield return Doc(WireValues.Sales, "INV-7", "INVOICE", "Sales Invoice", today.AddYears(-2), null);
                break;
            case Vins.Complete or Vins.Partial or Vins.Stale or Vins.Unavailable or Vins.CircuitOpen:
                yield return Doc(WireValues.Sales, "INV-1", "INVOICE", "Sales Invoice", today.AddYears(-3), null);
                yield return Doc(WireValues.Sales, "INS-1", "INSURANCE", "Comprehensive Insurance", today.AddMonths(-6), today.AddMonths(6));
                yield return Doc(WireValues.Sales, "WAR-1", "WARRANTY", "Extended Warranty", today.AddYears(-3), today.AddYears(2));
                yield return Doc(WireValues.Service, "PUC-1", "PUC", "Pollution Under Control certificate", today.AddMonths(-2), today.AddMonths(4));
                yield return Doc(WireValues.Service, "SH-1", "SERVICE_HISTORY", "Periodic maintenance 20,000 km", today.AddMonths(-2), null);
                yield return Doc(WireValues.Service, "JC-1", "JOB_CARD", "First free service", today.AddYears(-3).AddMonths(3), null);
                break;
            default:
                yield break; // Vins.Empty and any other valid VIN: both sources answer with nothing (D-004)
        }
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string title, string detail, string traceId, IReadOnlyList<SourceFailure>? sources)
    {
        var problem = new ProblemResponse(null, title, (int)status, detail, null, traceId, sources);
        var response = Json(status, problem, WebJsonContext.Default.ProblemResponse, "application/problem+json");
        response.Headers.Add(DocumentsApiClient.TraceIdHeader, traceId);
        return response;
    }

    private static HttpResponseMessage Json<T>(HttpStatusCode status, T body, JsonTypeInfo<T> type, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body, type), Encoding.UTF8, mediaType) };
}
