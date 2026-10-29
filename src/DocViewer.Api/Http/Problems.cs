using DocViewer.Api.Contracts;
using DocViewer.Application.Documents;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DocViewer.Api.Http;

/// <summary>RFC 7807 problem responses (DESIGN.md §7.3). <c>traceId</c> is added by the ProblemDetails customisation.</summary>
internal static class Problems
{
    private const string TypeBase = "https://docviewer.local/problems/";

    /// <summary>Seconds a client should wait before retrying a 503. Short: breakers re-probe within 30 s.</summary>
    public const int RetryAfterSeconds = 5;

    public static ProblemHttpResult InvalidVin(string detail, HttpContext context) =>
        TypedResults.Problem(detail, context.Request.Path, StatusCodes.Status400BadRequest, "Invalid VIN", TypeBase + "invalid-vin");

    public static ProblemHttpResult InvalidFilter(string detail, HttpContext context) =>
        TypedResults.Problem(detail, context.Request.Path, StatusCodes.Status400BadRequest, "Invalid filter", TypeBase + "invalid-filter");

    public static ProblemHttpResult TooManyRequests(HttpContext context) =>
        TypedResults.Problem(
            "Too many searches from this client. Wait for the time in Retry-After, then try again.",
            context.Request.Path,
            StatusCodes.Status429TooManyRequests,
            "Too many requests",
            TypeBase + "too-many-requests");

    public static ProblemHttpResult SourcesUnavailable(SearchResult result, HttpContext context)
    {
        context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sources = result.Documents.Sources
            .Select(s => new SourceFailure(s.Source, s.Status, s.Error))
            .ToList();

        return TypedResults.Problem(
            "No document source answered and no recent snapshot is available. Try again shortly.",
            context.Request.Path,
            StatusCodes.Status503ServiceUnavailable,
            "Document sources unavailable",
            TypeBase + "sources-unavailable",
            new Dictionary<string, object?> { ["sources"] = sources });
    }
}
