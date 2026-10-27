using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DocViewer.Domain;

namespace DocViewer.Api.Contracts;

/// <summary>
/// Optional <c>source</c>/<c>type</c>/<c>status</c> filters, applied after merge (DESIGN.md §7.1).
/// Values use the wire spelling (<c>EXPIRING_SOON</c>), case-insensitive; an empty set means "no filter".
/// </summary>
internal sealed record DocumentFilter(
    IReadOnlySet<SourceSystem> Sources,
    IReadOnlySet<DocumentType> Types,
    IReadOnlySet<DocumentStatus> Statuses)
{
    public bool Matches(Document document) =>
        (Sources.Count == 0 || Sources.Contains(document.Source))
        && (Types.Count == 0 || Types.Contains(document.Type))
        && (Statuses.Count == 0 || Statuses.Contains(document.Status));

    public static bool TryCreate(
        string[]? source,
        string[]? type,
        string[]? status,
        [NotNullWhen(true)] out DocumentFilter? filter,
        [NotNullWhen(false)] out string? error)
    {
        filter = null;
        if (!TryParseAll<SourceSystem>("source", source, out var sources, out error)
            || !TryParseAll<DocumentType>("type", type, out var types, out error)
            || !TryParseAll<DocumentStatus>("status", status, out var statuses, out error))
        {
            return false;
        }

        filter = new DocumentFilter(sources, types, statuses);
        return true;
    }

    private static bool TryParseAll<TEnum>(string name, string[]? values, out HashSet<TEnum> parsed, [NotNullWhen(false)] out string? error)
        where TEnum : struct, Enum
    {
        parsed = [];
        error = null;
        var allowed = Enum.GetValues<TEnum>().ToDictionary(v => WireName(v), StringComparer.OrdinalIgnoreCase);

        // Accept both repeated (?type=PUC&type=RC) and comma-separated (?type=PUC,RC) forms.
        foreach (var value in (values ?? []).SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        {
            if (!allowed.TryGetValue(value, out var item))
            {
                error = $"Unknown {name} '{value}'. Allowed: {string.Join(", ", allowed.Keys)}.";
                return false;
            }

            parsed.Add(item);
        }

        return true;
    }

    public static string WireName<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString());
}
