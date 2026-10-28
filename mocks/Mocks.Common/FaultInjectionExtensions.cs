using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mocks.Common;

public static class FaultInjectionExtensions
{
    /// <summary>Lets the demo UI's health panel flip the fault switch from the browser (D5.2). Admin endpoints only.</summary>
    public const string AdminCorsPolicy = "fault-admin";

    private static readonly string[] DefaultUiOrigins = ["http://localhost:5173"];

    public static IServiceCollection AddFaultInjection(this IServiceCollection services)
    {
        services.AddSingleton<FaultState>();
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IConfiguration>((cors, config) =>
        {
            var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() is { Length: > 0 } configured ? configured : DefaultUiOrigins;
            cors.AddPolicy(AdminCorsPolicy, p => p.WithOrigins(origins).WithMethods("GET", "POST").WithHeaders("Content-Type"));
        });
        services.Configure<JsonOptions>(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        return services;
    }

    /// <summary>Adds the fault middleware plus <c>GET/POST /__admin/fault</c> and <c>GET /healthz</c>.</summary>
    public static WebApplication UseFaultInjection(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseCors();
        app.UseMiddleware<FaultInjectionMiddleware>();

        var admin = app.MapGroup("/__admin").RequireCors(AdminCorsPolicy);
        admin.MapGet("/fault", (FaultState state) => state.Current);

        admin.MapPost("/fault", (FaultSettings settings, FaultState state) =>
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
