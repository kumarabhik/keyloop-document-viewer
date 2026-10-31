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
    IReadOnlyDictionary<string, string> Metadata,
    string? DealerCode = null); // D-034; the demo UI does not show it yet
