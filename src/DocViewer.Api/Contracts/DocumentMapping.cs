using DocViewer.Application.Documents;
using DocViewer.Domain;

namespace DocViewer.Api.Contracts;

internal static class DocumentMapping
{
    public static DocumentsResponse ToResponse(this SearchResult result, DocumentFilter filter, PageRequest page, string traceId)
    {
        var aggregated = result.Documents;
        var documents = aggregated.Documents.Where(filter.Matches).ToList(); // already in DocumentPosition order (D-012)
        var (items, nextCursor) = Paginate(documents, page);

        return new DocumentsResponse(
            aggregated.Vin.Value,
            aggregated.Partial,
            aggregated.GeneratedAt,
            result.Cache,
            traceId,
            [.. aggregated.Sources.Select(s => new SourceStatusResponse(
                s.Source, s.Status, (long)Math.Round(s.Latency.TotalMilliseconds), s.Documents.Count, s.AsOf, s.Error))],
            // Counts the whole filtered result, not just this page; without a limit the page is the whole result (D-031).
            new DocumentSummary(
                documents.Count,
                documents.Count(d => d.Status == DocumentStatus.Expired),
                documents.Count(d => d.Status == DocumentStatus.ExpiringSoon),
                documents.Count(d => d.Flags.Contains(DocumentMarker.PossibleDuplicate)),
                documents.Count(d => d.Flags.Contains(DocumentMarker.Conflict))),
            [.. items.Select(ToResponse)],
            nextCursor);
    }

    /// <summary>Keyset paging: documents after the cursor position, then at most <c>limit</c> of them (D-031).</summary>
    private static (List<Document> Items, string? NextCursor) Paginate(List<Document> documents, PageRequest page)
    {
        var after = page.After is { } position
            ? documents.Where(d => DocumentPosition.Of(d) > position).ToList()
            : documents;
        if (page.Limit is not { } limit || after.Count <= limit)
        {
            return (after, null);
        }

        var items = after.Take(limit).ToList();
        return (items, PageRequest.Encode(DocumentPosition.Of(items[^1])));
    }

    private static DocumentResponse ToResponse(Document d) =>
        new(d.Id, d.SourceId, d.Vin.Value, d.Title, d.Type, d.Date, d.ExpiryDate, d.Source, d.Status, d.Flags, d.Metadata, d.DealerCode);
}
