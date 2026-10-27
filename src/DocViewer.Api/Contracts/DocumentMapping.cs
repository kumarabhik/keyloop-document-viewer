using DocViewer.Application.Documents;
using DocViewer.Domain;

namespace DocViewer.Api.Contracts;

internal static class DocumentMapping
{
    public static DocumentsResponse ToResponse(this SearchResult result, DocumentFilter filter, string traceId)
    {
        var aggregated = result.Documents;
        var documents = aggregated.Documents.Where(filter.Matches).Select(ToResponse).ToList();

        return new DocumentsResponse(
            aggregated.Vin.Value,
            aggregated.Partial,
            aggregated.GeneratedAt,
            result.Cache,
            traceId,
            [.. aggregated.Sources.Select(s => new SourceStatusResponse(
                s.Source, s.Status, (long)Math.Round(s.Latency.TotalMilliseconds), s.Documents.Count, s.AsOf, s.Error))],
            new DocumentSummary(
                documents.Count,
                documents.Count(d => d.Status == DocumentStatus.Expired),
                documents.Count(d => d.Status == DocumentStatus.ExpiringSoon),
                documents.Count(d => d.Flags.Contains(DocumentMarker.PossibleDuplicate)),
                documents.Count(d => d.Flags.Contains(DocumentMarker.Conflict))),
            documents);
    }

    private static DocumentResponse ToResponse(Document d) =>
        new(d.Id, d.SourceId, d.Vin.Value, d.Title, d.Type, d.Date, d.ExpiryDate, d.Source, d.Status, d.Flags, d.Metadata);
}
