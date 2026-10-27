using System.Text.Json.Nodes;
using Mocks.Common;

// Mock of the workshop Service System (DESIGN.md §7.5): snake_case JSON, dd/MM/yyyy dates,
// and an empty "records" list (not 404) for unknown VINs — deliberately unlike the Sales mock.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFaultInjection();
builder.Services.AddSingleton(_ =>
    SeedStore.Load(Path.Combine(AppContext.BaseDirectory, "seed", "service.json"), "dd/MM/yyyy", TimeProvider.System));

var app = builder.Build();

app.UseFaultInjection();

app.MapGet("/service/api/records", (string? vin, SeedStore seed) =>
{
    if (string.IsNullOrWhiteSpace(vin))
    {
        return Results.BadRequest(new { error = "missing_vin" });
    }

    var body = seed.TryGet(vin, out var vehicle) ? vehicle!.ToJsonString() : new JsonObject { ["records"] = new JsonArray() }.ToJsonString();
    return Results.Text(body, "application/json");
});

await app.RunAsync();
