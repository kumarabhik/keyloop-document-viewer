namespace DocViewer.Infrastructure.Persistence;

/// <summary>Row of <c>search_audit</c> (DESIGN.md §8.3). Never holds a raw VIN (D-011).</summary>
public sealed class SearchAuditEntity
{
    public long Id { get; set; }
    public required string TraceId { get; set; }
    public required string VinHash { get; set; }
    public required string Outcome { get; set; }
    public bool CacheHit { get; set; }
    public int LatencyMs { get; set; }
    public required string SourceStatusJson { get; set; }
    public int DocumentCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
