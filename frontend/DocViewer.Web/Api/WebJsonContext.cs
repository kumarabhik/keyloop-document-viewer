using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocViewer.Web.Api;

/// <summary>Source-generated JSON (AGENTS.md §6); also keeps the trimmed WebAssembly build reflection-free.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DocumentsResponse))]
[JsonSerializable(typeof(ProblemResponse))]
[JsonSerializable(typeof(IReadOnlyList<SourceHealthResponse>))]
[JsonSerializable(typeof(FaultRequest))]
internal sealed partial class WebJsonContext : JsonSerializerContext;
