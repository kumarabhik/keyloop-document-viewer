using DocViewer.Application.Sources;
using DocViewer.Domain;

namespace DocViewer.Api.Contracts;

/// <summary>One row of <c>GET /api/v1/sources/health</c>. The <c>last*</c> fields are null until the source is first called.</summary>
public sealed record SourceHealthResponse(
    SourceSystem Name,
    CircuitState Circuit,
    SourceStatus? LastStatus,
    long? LastLatencyMs,
    DateTimeOffset? LastCheckedAt);

/// <summary>Per-source entry in the 503 problem's <c>sources</c> extension.</summary>
public sealed record SourceFailure(SourceSystem Name, SourceStatus Status, string? Error);
