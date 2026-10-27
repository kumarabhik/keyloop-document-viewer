namespace DocViewer.Domain;

/// <summary>
/// A document in canonical form, independent of the system it came from.
/// Adapters create it; <see cref="DocumentMerger"/> sets <see cref="Status"/> and <see cref="Flags"/>.
/// </summary>
public sealed record Document
{
    public required string SourceId { get; init; }
    public required SourceSystem Source { get; init; }
    public required Vin Vin { get; init; }
    public required string Title { get; init; }
    public required DocumentType Type { get; init; }
    public required DateOnly Date { get; init; }
    public DateOnly? ExpiryDate { get; init; }

    /// <summary>
    /// Business reference shared across systems (e.g. invoice number), used to detect duplicates.
    /// Each adapter knows how to extract it from its own payload.
    /// </summary>
    public string? ReferenceNumber { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public DocumentStatus Status { get; init; } = DocumentStatus.NotApplicable;
    public IReadOnlyList<DocumentMarker> Flags { get; init; } = [];

    /// <summary>Globally unique, stable id: <c>SALES:INV-2025-0091</c>.</summary>
    public string Id => $"{Source.ToString().ToUpperInvariant()}:{SourceId}";
}
