namespace DocViewer.Web.Api;

/// <summary>
/// Client copy of the API's <c>DocumentsResponse</c> (DESIGN.md §7.2). Enum values stay wire strings
/// (<c>SALES</c>, <c>circuit_open</c>) so a new value never breaks the page. <c>WireContractTests</c>
/// fails if these records drift from <c>docs/openapi.json</c> (D-025).
/// </summary>
public sealed record DocumentsResponse(
    string Vin,
    bool Partial,
    DateTimeOffset GeneratedAt,
    string Cache,
    string TraceId,
    IReadOnlyList<SourceStatusResponse> Sources,
    DocumentSummary Summary,
    IReadOnlyList<DocumentResponse> Documents);
