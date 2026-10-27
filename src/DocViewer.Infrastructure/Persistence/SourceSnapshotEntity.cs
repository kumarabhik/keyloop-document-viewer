namespace DocViewer.Infrastructure.Persistence;

/// <summary>Row of <c>source_snapshot</c> (DESIGN.md §8.3).</summary>
public sealed class SourceSnapshotEntity
{
    public required string Vin { get; set; }
    public required string Source { get; set; }

    /// <summary>Canonical documents as JSON (<c>jsonb</c> on PostgreSQL, <c>TEXT</c> on SQLite).</summary>
    public required string DocumentsJson { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
