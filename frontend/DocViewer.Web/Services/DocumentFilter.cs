using DocViewer.Web.Api;

namespace DocViewer.Web.Services;

/// <summary>
/// The table filters, mirrored in the URL (<c>?source=SALES&amp;type=PUC&amp;status=EXPIRED</c>, D5.3). Applied on the client
/// to the full result, so alerts and counts always describe the whole vehicle (D-025). Unknown values are ignored.
/// </summary>
public sealed record DocumentFilter(string? Source, string? Type, string? Status)
{
    public static DocumentFilter None { get; } = new(null, null, null);

    public bool IsEmpty => Source is null && Type is null && Status is null;

    public static DocumentFilter FromQuery(string? source, string? type, string? status) =>
        new(Known(source, WireValues.SourceNames), Known(type, WireValues.DocumentTypes), Known(status, WireValues.DocumentStatuses));

    public IReadOnlyList<DocumentResponse> Apply(IEnumerable<DocumentResponse> documents) =>
        documents.Where(d => (Source is null || d.Source == Source)
                             && (Type is null || d.Type == Type)
                             && (Status is null || d.Status == Status)).ToList();

    public IReadOnlyDictionary<string, object?> ToQuery() =>
        new Dictionary<string, object?> { ["source"] = Source, ["type"] = Type, ["status"] = Status };

    private static string? Known(string? value, IReadOnlyList<string> allowed) =>
        allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
