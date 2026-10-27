using System.Text.Json;
using System.Text.Json.Serialization;
using DocViewer.Application.Sources;
using DocViewer.Domain;

namespace DocViewer.Infrastructure.Persistence;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<Document>))]
[JsonSerializable(typeof(Dictionary<SourceSystem, SourceStatus>))]
internal sealed partial class PersistenceJsonContext : JsonSerializerContext;
