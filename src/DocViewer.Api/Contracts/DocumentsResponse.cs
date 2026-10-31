using System.ComponentModel;
using System.Text.Json.Serialization;
using DocViewer.Application.Documents;
using DocViewer.Application.Sources;
using DocViewer.Domain;

namespace DocViewer.Api.Contracts;

/// <summary>Body of <c>GET /api/v1/vehicles/{vin}/documents</c> (DESIGN.md §7.2).</summary>
public sealed record DocumentsResponse(
    string Vin,
    [property: Description(
        "True when at least one source is not `ok`. The documents are then incomplete or partly stale: " +
        "check `sources[].status` and show the user which system is missing or out of date. Still HTTP 200.")]
    bool Partial,
    DateTimeOffset GeneratedAt,
    CacheStatus Cache,
    string TraceId,
    IReadOnlyList<SourceStatusResponse> Sources,
    [property: Description("Counts over every document that matches the filters, on all pages together.")]
    DocumentSummary Summary,
    IReadOnlyList<DocumentResponse> Documents,
    [property: Description(
        "Pass as `cursor` (with the same filters and `limit`) to get the next page. Null on the last page and whenever " +
        "`limit` is not set (D-031).")]
    string? NextCursor);

public sealed record SourceStatusResponse(
    SourceSystem Name,
    [property: Description(
        "`ok`: this source answered. `stale`: it failed, and its documents come from the last saved copy (see `asOf`). " +
        "`timeout`, `error`, `circuit_open`: it failed and none of its documents are in this response.")]
    SourceStatus Status,
    long LatencyMs,
    int Count,
    [property: Description("When the saved copy was taken; set only when `status` is `stale`. Copies older than 24 h (default) are never served.")]
    DateTimeOffset? AsOf,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error);

/// <summary>Counts over the documents in this response (after filters).</summary>
public sealed record DocumentSummary(int Total, int Expired, int ExpiringSoon, int Duplicates, int Conflicts);

public sealed record DocumentResponse(
    string Id,
    string SourceId,
    string Vin,
    string Title,
    DocumentType Type,
    DateOnly Date,
    DateOnly? ExpiryDate,
    SourceSystem Source,
    DocumentStatus Status,
    IReadOnlyList<DocumentMarker> Flags,
    IReadOnlyDictionary<string, string> Metadata,
    [property: System.ComponentModel.Description("Dealership the document belongs to; null when the source system did not say (D-034).")]
    string? DealerCode);
