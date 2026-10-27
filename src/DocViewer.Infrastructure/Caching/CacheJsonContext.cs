using System.Text.Json;
using System.Text.Json.Serialization;
using DocViewer.Application.Documents;

namespace DocViewer.Infrastructure.Caching;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AggregatedDocuments))]
internal sealed partial class CacheJsonContext : JsonSerializerContext;
