namespace DocViewer.Domain;

/// <summary>
/// Turns documents from all sources into one list: computes status, flags cross-source
/// duplicates and conflicts, and sorts deterministically (DESIGN.md §9.2–9.4).
/// Documents are never removed or merged (D-009).
/// </summary>
public static class DocumentMerger
{
    public const string DuplicateOfKey = "duplicateOf";
    public const string ConflictsWithKey = "conflictsWith";

    public static IReadOnlyList<Document> Merge(IEnumerable<Document> documents, DateOnly today, int warningDays)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var withStatus = documents
            .Select(d => d with { Status = DocumentStatusCalculator.Calculate(d.ExpiryDate, today, warningDays) })
            .ToList();

        var matchedIds = FindCrossSourceMatches(withStatus);

        return withStatus
            .Select(d => matchedIds.TryGetValue(d.Id, out var others) ? Annotate(d, others) : d)
            .OrderBy(DocumentPosition.Of) // D-012, the same order pagination cursors use (D-031)
            .ToList();
    }

    /// <summary>Maps document id → documents from *other* sources that describe the same paper.</summary>
    private static Dictionary<string, List<Document>> FindCrossSourceMatches(List<Document> documents)
    {
        var matches = new Dictionary<string, List<Document>>();

        foreach (var group in documents.Where(d => MatchKey(d) is not null).GroupBy(MatchKey))
        {
            foreach (var document in group)
            {
                var others = group.Where(o => o.Source != document.Source).ToList();
                if (others.Count > 0)
                {
                    matches[document.Id] = others;
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Same type + same reference number. Without a reference, invoices and warranties fall back to
    /// type + issue date, since those rarely share a date by coincidence for one vehicle.
    /// </summary>
    private static string? MatchKey(Document document)
    {
        var reference = document.ReferenceNumber?.Trim();
        if (!string.IsNullOrEmpty(reference))
        {
            return $"{document.Type}|ref|{reference.ToUpperInvariant()}";
        }

        return document.Type is DocumentType.Invoice or DocumentType.Warranty
            ? $"{document.Type}|date|{document.Date:yyyy-MM-dd}"
            : null;
    }

    private static Document Annotate(Document document, List<Document> others)
    {
        var sameDates = others.TrueForAll(o => o.Date == document.Date && o.ExpiryDate == document.ExpiryDate);
        var marker = sameDates ? DocumentMarker.PossibleDuplicate : DocumentMarker.Conflict;
        var metadataKey = sameDates ? DuplicateOfKey : ConflictsWithKey;

        var metadata = new Dictionary<string, string>(document.Metadata)
        {
            [metadataKey] = string.Join(',', others.Select(o => o.Id).Order(StringComparer.Ordinal)),
        };

        return document with
        {
            Flags = [.. document.Flags.Where(f => f != marker), marker],
            Metadata = metadata,
        };
    }
}
