using System.Text.Json;
using System.Text.Json.Serialization;
using DocViewer.Application.Sources;

namespace DocViewer.Api.Contracts;

/// <summary>
/// Enum spelling on the wire (DESIGN.md §7.2): source and circuit states are lower snake case
/// (<c>circuit_open</c>, <c>half_open</c>); every other enum is upper snake case (<c>EXPIRING_SOON</c>, <c>SALES</c>).
/// The specific converters must come before the general one.
/// </summary>
internal static class ApiJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
        options.Converters.Add(new JsonStringEnumConverter<SourceStatus>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<CircuitState>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DocumentsResponse))]
[JsonSerializable(typeof(IReadOnlyList<SourceHealthResponse>))]
[JsonSerializable(typeof(List<SourceFailure>))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
