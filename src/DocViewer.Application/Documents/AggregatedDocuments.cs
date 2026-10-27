using DocViewer.Application.Sources;
using DocViewer.Domain;

namespace DocViewer.Application.Documents;

/// <summary>Result of one VIN search across all sources, before HTTP concerns (filters, headers).</summary>
public sealed record AggregatedDocuments
{
    public required Vin Vin { get; init; }
    public required IReadOnlyList<SourceResult> Sources { get; init; }
    public required IReadOnlyList<Document> Documents { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>True when any source did not answer live (DESIGN.md §9.5, invariant 3).</summary>
    public bool Partial => Sources.Any(s => !s.IsOk);
}
