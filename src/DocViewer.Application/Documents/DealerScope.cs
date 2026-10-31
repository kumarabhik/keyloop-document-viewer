using DocViewer.Domain;

namespace DocViewer.Application.Documents;

/// <summary>
/// The dealerships a caller may see (D-034), from the token's dealer claims. A document is visible only when its
/// <see cref="Document.DealerCode"/> is one of them; a document without a dealer code is hidden (fail closed).
/// </summary>
public sealed record DealerScope
{
    public DealerScope(IEnumerable<string> dealerCodes)
    {
        ArgumentNullException.ThrowIfNull(dealerCodes);
        DealerCodes = dealerCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> DealerCodes { get; }

    public bool Allows(Document document) =>
        document is { DealerCode: { } code } && DealerCodes.Contains(code);

    /// <summary>
    /// The part of <paramref name="all"/> this scope may see. Per-source counts shrink with it, and duplicate/conflict
    /// marks are worked out again among the visible documents only, so neither reveals a hidden document.
    /// Partial status and source health are unchanged: they describe the systems, not the documents.
    /// </summary>
    public AggregatedDocuments Apply(AggregatedDocuments all, DateOnly today, int warningDays)
    {
        ArgumentNullException.ThrowIfNull(all);
        var visible = all.Documents.Where(Allows).Select(DocumentMerger.ClearCrossSourceMarks);
        return all with
        {
            Sources = [.. all.Sources.Select(s => s with { Documents = [.. s.Documents.Where(Allows)] })],
            Documents = DocumentMerger.Merge(visible, today, warningDays),
        };
    }
}
