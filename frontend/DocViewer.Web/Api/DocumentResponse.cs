namespace DocViewer.Web.Api;

public sealed record DocumentResponse(
    string Id,
    string SourceId,
    string Vin,
    string Title,
    string Type,
    DateOnly Date,
    DateOnly? ExpiryDate,
    string Source,
    string Status,
    IReadOnlyList<string> Flags,
    IReadOnlyDictionary<string, string> Metadata);
