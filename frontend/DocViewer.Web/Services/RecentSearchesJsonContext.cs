using System.Text.Json.Serialization;

namespace DocViewer.Web.Services;

[JsonSerializable(typeof(List<string>))]
internal sealed partial class RecentSearchesJsonContext : JsonSerializerContext;
