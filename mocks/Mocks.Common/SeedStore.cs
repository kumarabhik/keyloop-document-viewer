using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mocks.Common;

/// <summary>
/// Seed data keyed by VIN, loaded from a JSON object file. Dates may be written as relative tokens
/// such as <c>{{today+10d}}</c> so expiry scenarios stay correct whenever the demo runs (D-018).
/// </summary>
public sealed partial class SeedStore
{
    private readonly Dictionary<string, JsonNode> _byVin;

    private SeedStore(Dictionary<string, JsonNode> byVin) => _byVin = byVin;

    public int Count => _byVin.Count;

    public static SeedStore Load(string path, string dateFormat, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var json = ResolveDateTokens(File.ReadAllText(path), today, dateFormat);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException($"Seed file '{path}' must contain a JSON object keyed by VIN.");

        var byVin = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var (vin, node) in root)
        {
            if (node is not null)
            {
                byVin[vin] = node.DeepClone();
            }
        }

        return new SeedStore(byVin);
    }

    public bool TryGet(string vin, out JsonNode? node) => _byVin.TryGetValue(vin.Trim(), out node);

    public static string ResolveDateTokens(string json, DateOnly today, string dateFormat) =>
        DateTokenRegex().Replace(json, match =>
        {
            var offset = match.Groups["offset"].Success
                ? int.Parse(match.Groups["offset"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
                : 0;
            return today.AddDays(offset).ToString(dateFormat, CultureInfo.InvariantCulture);
        });

    [GeneratedRegex(@"\{\{today(?<offset>[+-]\d+)?d?\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex DateTokenRegex();
}
