using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DocViewer.Api.Endpoints;
using DocViewer.Domain;

namespace DocViewer.Api.Tests;

/// <summary>
/// D-026: what the contract fuzzer (Schemathesis, CI) found, pinned as fast tests on the served OpenAPI document.
/// </summary>
public sealed class OpenApiContractTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public OpenApiContractTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<JsonNode> DocumentAsync() =>
        JsonNode.Parse(await _client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)))!;

    private static JsonNode Parameter(JsonNode spec, string name) =>
        spec["paths"]!["/api/v1/vehicles/{vin}/documents"]!["get"]!["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;

    [Fact]
    public async Task OptionalWhenNullProperties_AreNotRequired()
    {
        // A healthy source has no "error"; the schema used to require it, so strict clients would reject 200s.
        var required = (await DocumentAsync())["components"]!["schemas"]!["SourceStatusResponse"]!["required"]!.AsArray()
            .Select(r => r!.GetValue<string>()).ToList();

        required.ShouldNotContain("error");
        required.ShouldContain("status");
    }

    [Theory]
    [InlineData("DocumentsResponse", "partial", "sources")]
    [InlineData("SourceStatusResponse", "status", "stale")]
    [InlineData("SourceStatusResponse", "asOf", "stale")]
    public async Task ResponseFields_TellClientsHowToReadAPartialAnswer(string schema, string property, string mustMention)
    {
        // R7: a client generated from the spec must learn to check `partial` and each source's status.
        var description = (await DocumentAsync())["components"]!["schemas"]![schema]!["properties"]![property]!["description"]?.GetValue<string>();

        description.ShouldNotBeNullOrWhiteSpace();
        description.ShouldContain(mustMention, Case.Insensitive);
    }

    [Fact]
    public async Task LimitParameter_IsAnIntegerFrom1To100()
    {
        // D-031: the generator's integer-or-string schema let "-64" count as valid; the fuzzer found it.
        var schema = Parameter(await DocumentAsync(), "limit")["schema"]!;

        schema["type"]!.GetValue<string>().ShouldBe("integer");
        schema["minimum"]!.GetValue<int>().ShouldBe(1);
        schema["maximum"]!.GetValue<int>().ShouldBe(100);
        schema["pattern"].ShouldBeNull();
    }

    [Fact]
    public async Task VinParameter_DocumentsTheCanonicalPattern()
    {
        Parameter(await DocumentAsync(), "vin")["schema"]!["pattern"]!.GetValue<string>().ShouldBe(DocumentParametersTransformer.VinPattern);
    }

    [Theory]
    [InlineData("MA3EWB81S00100001", true)]
    [InlineData("MA3EWB81S0010000O", false)] // O is never valid
    [InlineData("MA3EWB81S0010000", false)]
    public void VinPattern_AgreesWithVinTryParse_OnCanonicalInput(string input, bool valid)
    {
        Regex.IsMatch(input, DocumentParametersTransformer.VinPattern).ShouldBe(valid);
        Vin.TryParse(input, out _, out _).ShouldBe(valid);
    }

    [Theory]
    [InlineData("source", new[] { "SALES", "SERVICE" })]
    [InlineData("status", new[] { "NOT_APPLICABLE", "VALID", "EXPIRING_SOON", "EXPIRED" })]
    public async Task FilterParameters_ListTheAllowedValues(string name, string[] expected)
    {
        var values = Parameter(await DocumentAsync(), name)["schema"]!["items"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>());

        values.ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public async Task TypeFilter_ListsEveryDocumentType()
    {
        var values = Parameter(await DocumentAsync(), "type")["schema"]!["items"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>());

        values.Count().ShouldBe(Enum.GetValues<DocumentType>().Length);
        values.ShouldContain("SERVICE_HISTORY");
    }
}
