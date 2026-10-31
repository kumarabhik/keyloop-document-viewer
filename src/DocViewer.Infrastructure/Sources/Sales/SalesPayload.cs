using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocViewer.Infrastructure.Sources.Sales;

// Wire format of the Sales System (DESIGN.md Appendix B). Fields are nullable on purpose:
// a record missing data is dropped and counted, not allowed to break the whole response.

internal sealed record SalesDocumentsResponse(SalesVehicle? Vehicle, List<SalesDocument>? Documents);

internal sealed record SalesVehicle(string? Vin, string? Model, string? SoldOn, string? DealerCode);

internal sealed record SalesDocument(
    string? DocId,
    string? DocType,
    string? Title,
    string? IssuedOn,
    string? ValidUntil,
    Dictionary<string, JsonElement>? Meta);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(SalesDocumentsResponse))]
internal sealed partial class SalesJsonContext : JsonSerializerContext;
