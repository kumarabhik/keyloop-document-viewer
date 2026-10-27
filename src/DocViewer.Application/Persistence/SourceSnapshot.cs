using DocViewer.Domain;

namespace DocViewer.Application.Persistence;

public sealed record SourceSnapshot(SourceSystem Source, IReadOnlyList<Document> Documents, DateTimeOffset FetchedAt);
