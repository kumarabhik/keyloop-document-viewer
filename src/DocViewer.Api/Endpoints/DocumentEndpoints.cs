using System.ComponentModel;
using DocViewer.Api.Contracts;
using DocViewer.Api.Http;
using DocViewer.Application.Documents;
using DocViewer.Application.Persistence;
using DocViewer.Application.Sources;
using DocViewer.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocViewer.Api.Endpoints;

internal static class DocumentEndpoints
{
    public const string CacheHeader = "X-Cache";

    public const string GetDocumentsOperation = "GetVehicleDocuments";

    public static RouteGroupBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Documents");
        if (app.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value.Enabled)
        {
            api.RequireAuthorization(DealerAuthentication.PolicyName); // D-034
        }

        api.MapGet("/vehicles/{vin}/documents", GetDocumentsAsync)
            .WithName(GetDocumentsOperation)
            .WithSummary("Documents for a vehicle from all source systems")
            .WithDescription(
                "Calls the Sales and Service systems in parallel and returns one merged list. Each document names its source. " +
                "If a source fails, the response is still 200 with partial=true; 503 only when no source answered and no snapshot exists. " +
                "When auth is enabled (off by default), a bearer token is required and only documents of the token's dealer_id " +
                "dealerships are returned; refresh=true then needs the admin role.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(SearchRateLimiting.PolicyName);

        api.MapGet("/sources/health", GetSourceHealth)
            .WithName("GetSourceHealth")
            .WithSummary("Circuit-breaker state and last observed outcome per source")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return api;
    }

    internal static async Task<Results<Ok<DocumentsResponse>, ProblemHttpResult>> GetDocumentsAsync(
        [Description("17-character VIN; case-insensitive, surrounding spaces ignored. Letters I, O and Q are not allowed.")] string vin,
        [FromQuery, Description("Only these sources: SALES, SERVICE. Repeat or comma-separate.")] string[]? source,
        [FromQuery, Description("Only these types, e.g. INSURANCE, PUC, JOB_CARD. Repeat or comma-separate.")] string[]? type,
        [FromQuery, Description("Only these statuses: VALID, EXPIRING_SOON, EXPIRED, NOT_APPLICABLE.")] string[]? status,
        [FromQuery, Description("true skips the cache read; a complete result is still cached.")] bool? refresh,
        [FromQuery, Description("Optional page size, 1-100. Without it the whole list is returned (D-031).")] int? limit,
        [FromQuery, Description("The nextCursor of the previous page, unchanged. Only meaningful with the same filters.")] string? cursor,
        AggregationService aggregation,
        IOptions<AuthOptions> auth,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var traceId = TraceIds.Current(context);

        // Validate before any I/O: invalid input never reaches a source (invariant 6).
        if (!Vin.TryParse(vin, out var parsed, out var vinError))
        {
            aggregation.RecordInvalidSearch(vin, traceId);
            return Problems.InvalidVin(vinError, context);
        }

        if (!DocumentFilter.TryCreate(source, type, status, out var filter, out var filterError))
        {
            return Problems.InvalidFilter(filterError, context);
        }

        if (!PageRequest.TryCreate(limit, cursor, out var page, out var pageError))
        {
            return Problems.InvalidPage(pageError, context);
        }

        if (refresh == true && !DealerAuthentication.MayRefresh(context.User, auth.Value))
        {
            return Problems.RefreshForbidden(context);
        }

        var scope = DealerAuthentication.ScopeOf(context.User, auth.Value);
        var result = await aggregation.SearchAsync(new SearchRequest(parsed, refresh ?? false, traceId, scope), cancellationToken);
        if (result.Outcome == SearchOutcome.Failed)
        {
            return Problems.SourcesUnavailable(result, context);
        }

        context.Response.Headers[CacheHeader] = DocumentFilter.WireName(result.Cache);
        return TypedResults.Ok(result.ToResponse(filter, page, traceId));
    }

    internal static async Task<Ok<IReadOnlyList<SourceHealthResponse>>> GetSourceHealth(
        ISourceHealth health, SourceActivity activity, CancellationToken cancellationToken)
    {
        IReadOnlyList<SourceHealthResponse> rows =
        [
            .. (await health.ReadHealthAsync(cancellationToken)).OrderBy(h => h.Source).Select(h =>
            {
                var last = activity.LastFor(h.Source);
                return new SourceHealthResponse(
                    h.Source,
                    h.Circuit,
                    last?.Status,
                    last is null ? null : (long)Math.Round(last.Latency.TotalMilliseconds),
                    last?.At);
            }),
        ];
        return TypedResults.Ok(rows);
    }
}
