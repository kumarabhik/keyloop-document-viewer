namespace DocViewer.Web.Api;

/// <summary>One row of <c>GET /api/v1/sources/health</c>; <c>last*</c> are null until the source is first called.</summary>
public sealed record SourceHealthResponse(string Name, string Circuit, string? LastStatus, long? LastLatencyMs, DateTimeOffset? LastCheckedAt);
