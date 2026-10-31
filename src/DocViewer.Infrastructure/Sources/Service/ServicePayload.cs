using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocViewer.Infrastructure.Sources.Service;

// Wire format of the Service System (DESIGN.md Appendix B): snake_case, dd/MM/yyyy dates.

internal sealed record ServiceRecordsResponse(List<ServiceRecord>? Records);

internal sealed record ServiceRecord(
    string? RecordNo,
    string? Category,
    string? Description,
    string? Created,
    string? Expiry,
    string? Workshop,
    string? DealerCode,
    int? OdometerKm);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ServiceRecordsResponse))]
internal sealed partial class ServiceJsonContext : JsonSerializerContext;
