using System.Text.Json.Serialization;
using DocViewer.Application.Documents;
using DocViewer.Application.Sources;
using DocViewer.Domain;

namespace DocViewer.Api.Contracts;

/// <summary>Body of <c>GET /api/v1/vehicles/{vin}/documents</c> (DESIGN.md §7.2).</summary>
public sealed record DocumentsResponse(
    string Vin,
    bool Partial,
    DateTimeOffset GeneratedAt,
    CacheStatus Cache,
    string TraceId,
    IReadOnlyList<SourceStatusResponse> Sources,
    DocumentSummary Summary,
    IReadOnlyList<DocumentResponse> Documents);

public sealed record SourceStatusResponse(
    SourceSystem Name,
    SourceStatus Status,
    long LatencyMs,
    int Count,
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
    IReadOnlyDictionary<string, string> Metadata);
