using Mocks.Common;

// Mock of the dealership Sales System (DESIGN.md §7.5): camelCase JSON, ISO dates, 404 for unknown VINs.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFaultInjection();
builder.Services.AddSingleton(_ =>
    SeedStore.Load(Path.Combine(AppContext.BaseDirectory, "seed", "sales.json"), "yyyy-MM-dd", TimeProvider.System));

var app = builder.Build();

app.UseFaultInjection();

app.MapGet("/sales/v1/vehicles/{vin}/documents", (string vin, SeedStore seed) =>
    seed.TryGet(vin, out var vehicle)
        ? Results.Text(vehicle!.ToJsonString(), "application/json")
        : Results.NotFound(new { error = "vehicle_not_found", vin }));

await app.RunAsync();
