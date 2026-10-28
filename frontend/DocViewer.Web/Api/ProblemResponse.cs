namespace DocViewer.Web.Api;

/// <summary>RFC 7807 problem (DESIGN.md §7.3) with the API's <c>traceId</c> and, on 503, <c>sources</c> extensions.</summary>
public sealed record ProblemResponse(
    string? Type,
    string? Title,
    int? Status,
    string? Detail,
    string? Instance,
    string? TraceId,
    IReadOnlyList<SourceFailure>? Sources);
