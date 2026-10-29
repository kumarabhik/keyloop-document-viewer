using System.Globalization;
using System.Text.Json.Nodes;
using DocViewer.Api.Contracts;
using DocViewer.Domain;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DocViewer.Api.Endpoints;

/// <summary>
/// Puts the documents endpoint's input rules into <c>docs/openapi.json</c>, so clients and the contract fuzzer know
/// them (D-026): the canonical VIN format and the allowed filter values, taken from the same enums the API parses.
/// The API stays more lenient than this (lower case, spaces around the VIN, comma lists; D-021).
/// </summary>
internal sealed class DocumentParametersTransformer : IOpenApiOperationTransformer
{
    /// <summary>The canonical form <see cref="Vin.TryParse"/> produces (D-008: no check digit).</summary>
    public const string VinPattern = "^[A-HJ-NPR-Z0-9]{17}$";

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.OperationId != DocumentEndpoints.GetDocumentsOperation || operation.Parameters is null)
        {
            return Task.CompletedTask;
        }

        foreach (var parameter in operation.Parameters)
        {
            switch (parameter.Name, parameter.Schema)
            {
                case ("vin", OpenApiSchema vin):
                    vin.Pattern = VinPattern;
                    break;
                case ("source", OpenApiSchema { Items: OpenApiSchema items }):
                    items.Enum = WireNames<SourceSystem>();
                    break;
                case ("type", OpenApiSchema { Items: OpenApiSchema items }):
                    items.Enum = WireNames<DocumentType>();
                    break;
                case ("status", OpenApiSchema { Items: OpenApiSchema items }):
                    items.Enum = WireNames<DocumentStatus>();
                    break;
                case ("limit", OpenApiSchema limit):
                    // The generator emits integer-or-string with a pattern that allows "-64"; minimum/maximum bind only
                    // the integer branch, so the fuzzer sent -64 as "valid" (D-031). A page size is an integer.
                    limit.Type = JsonSchemaType.Integer;
                    limit.Pattern = null;
                    limit.Minimum = "1";
                    limit.Maximum = PageRequest.MaxLimit.ToString(CultureInfo.InvariantCulture);
                    break;
            }
        }

        return Task.CompletedTask;
    }

    private static List<JsonNode> WireNames<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(v => (JsonNode)JsonValue.Create(DocumentFilter.WireName(v)))];
}
