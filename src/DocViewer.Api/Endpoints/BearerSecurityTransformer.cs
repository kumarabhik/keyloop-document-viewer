using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DocViewer.Api.Endpoints;

/// <summary>
/// Describes the optional bearer auth in <c>docs/openapi.json</c> (D-034). The document is generated with auth off, so
/// the scheme is optional on every <c>/api/v1</c> operation: no token (auth off) or a bearer token (auth on).
/// </summary>
internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Only when the API runs with Auth:Enabled=true (off by default): an OIDC access token whose " +
                "dealer_id claims name the dealerships whose documents may be returned (D-034).",
        };

        var bearer = new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeName, document)] = [] };
        foreach (var (path, item) in document.Paths)
        {
            if (!path.StartsWith("/api/v1/", StringComparison.Ordinal) || item.Operations is null)
            {
                continue;
            }

            foreach (var operation in item.Operations.Values)
            {
                operation.Security = [new OpenApiSecurityRequirement(), bearer]; // {} = anonymous, i.e. auth off
            }
        }

        return Task.CompletedTask;
    }
}
