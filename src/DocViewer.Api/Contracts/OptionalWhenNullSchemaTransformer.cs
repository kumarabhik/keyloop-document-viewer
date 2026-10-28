using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DocViewer.Api.Contracts;

/// <summary>
/// A property serialised with <c>JsonIgnoreCondition.WhenWritingNull</c> is left out of the response when it is null
/// (e.g. <c>sources[].error</c> for a healthy source), so the schema must not list it as required. The generator does
/// not know that and marked it required; the contract fuzzer caught it (D-026).
/// </summary>
internal sealed class OptionalWhenNullSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);
        if (schema.Required is not { Count: > 0 } required)
        {
            return Task.CompletedTask;
        }

        foreach (var property in context.JsonTypeInfo.Properties)
        {
            var ignore = property.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true)
                .OfType<JsonIgnoreAttribute>().FirstOrDefault();
            if (ignore?.Condition is JsonIgnoreCondition.WhenWritingNull or JsonIgnoreCondition.WhenWritingDefault)
            {
                required.Remove(property.Name);
            }
        }

        return Task.CompletedTask;
    }
}
