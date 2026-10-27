using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Mocks.Common;

public static class FaultInjectionExtensions
{
    public static IServiceCollection AddFaultInjection(this IServiceCollection services)
    {
        services.AddSingleton<FaultState>();
        services.Configure<JsonOptions>(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        return services;
    }

    /// <summary>Adds the fault middleware plus <c>GET/POST /__admin/fault</c> and <c>GET /healthz</c>.</summary>
    public static WebApplication UseFaultInjection(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<FaultInjectionMiddleware>();

        app.MapGet("/__admin/fault", (FaultState state) => state.Current);

        app.MapPost("/__admin/fault", (FaultSettings settings, FaultState state) =>
        {
            if (settings.Validate() is { } error)
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            state.Set(settings);
            return Results.Ok(settings);
        });

        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

        return app;
    }
}
