using DocViewer.Domain;

namespace DocViewer.Application.Sources;

/// <summary>
/// Outcome of asking one source for documents. Failures are values, not exceptions,
/// so one source can never abort the whole search (AGENTS.md §4 rule 3).
/// </summary>
public sealed record SourceResult
{
    public required SourceSystem Source { get; init; }
    public required SourceStatus Status { get; init; }
    public IReadOnlyList<Document> Documents { get; init; } = [];
    public TimeSpan Latency { get; init; }

    /// <summary>Short machine-friendly reason for a failure, e.g. "timeout" or "http_503".</summary>
    public string? Error { get; init; }

    /// <summary>When the data was fetched, if not live (stale fallback).</summary>
    public DateTimeOffset? AsOf { get; init; }

    /// <summary>Records the adapter skipped because they could not be mapped.</summary>
    public int DroppedRecords { get; init; }

    public bool IsOk => Status == SourceStatus.Ok;

    public static SourceResult Ok(SourceSystem source, IReadOnlyList<Document> documents, TimeSpan latency, int droppedRecords = 0) =>
        new() { Source = source, Status = SourceStatus.Ok, Documents = documents, Latency = latency, DroppedRecords = droppedRecords };

    public static SourceResult Failed(SourceSystem source, SourceStatus status, string error, TimeSpan latency)
    {
        if (status is SourceStatus.Ok or SourceStatus.Stale)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A failed result needs a failure status.");
        }

        return new() { Source = source, Status = status, Error = error, Latency = latency };
    }
}
