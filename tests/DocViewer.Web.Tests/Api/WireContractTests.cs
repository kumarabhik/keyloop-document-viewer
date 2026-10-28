using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocViewer.Web.Api;

namespace DocViewer.Web.Tests.Api;

/// <summary>
/// The client keeps small copies of the API's response records instead of generated code (D-025).
/// These tests fail when they drift from the committed contract, <c>docs/openapi.json</c>.
/// </summary>
public sealed class WireContractTests
{
    private static readonly JsonObject Schemas = LoadSchemas();

    [Theory]
    [InlineData(typeof(DocumentsResponse), "DocumentsResponse")]
    [InlineData(typeof(SourceStatusResponse), "SourceStatusResponse")]
    [InlineData(typeof(DocumentSummary), "DocumentSummary")]
    [InlineData(typeof(DocumentResponse), "DocumentResponse")]
    [InlineData(typeof(SourceHealthResponse), "SourceHealthResponse")]
    public void ClientRecord_HasExactlyTheSchemaProperties(Type record, string schema)
    {
        WireNames(record).ShouldBe(SchemaProperties(schema), ignoreOrder: true);
    }

    [Fact]
    public void ProblemResponse_IsProblemDetailsPlusTheApiExtensions()
    {
        WireNames(typeof(ProblemResponse)).ShouldBe([.. SchemaProperties("ProblemDetails"), "traceId", "sources"], ignoreOrder: true);
    }

    [Fact]
    public void WireValues_MatchTheSchemaEnums()
    {
        WireValues.SourceNames.ShouldBe(SchemaEnum("SourceSystem"), ignoreOrder: true);
        WireValues.DocumentTypes.ShouldBe(SchemaEnum("DocumentType"), ignoreOrder: true);
        WireValues.DocumentStatuses.ShouldBe(SchemaEnum("DocumentStatus"), ignoreOrder: true);
        SchemaEnum("DocumentMarker").ShouldContain(WireValues.Conflict);
        SchemaEnum("DocumentMarker").ShouldContain(WireValues.PossibleDuplicate);
        SchemaEnum("SourceStatus").ShouldContain(WireValues.Ok);
        SchemaEnum("SourceStatus").ShouldContain(WireValues.Stale);
    }

    // Positional records: the constructor parameters are the wire fields; helper properties (IsOk) are not.
    private static List<string> WireNames(Type record) =>
        record.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length).First()
            .GetParameters().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name!)).ToList();

    private static List<string> SchemaProperties(string schema) =>
        Schemas[schema]!["properties"]!.AsObject().Select(p => p.Key).ToList();

    private static List<string> SchemaEnum(string schema) =>
        Schemas[schema]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()).ToList();

    private static JsonObject LoadSchemas()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DocViewer.slnx")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("repository root (DocViewer.slnx) not found above the test output folder");
        var spec = JsonNode.Parse(File.ReadAllText(Path.Combine(dir.FullName, "docs", "openapi.json")))!;
        return spec["components"]!["schemas"]!.AsObject();
    }
}
