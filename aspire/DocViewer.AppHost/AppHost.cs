// One-click local run with the Aspire dashboard (backlog item, 2026-10-30):
//   dotnet run --project aspire/DocViewer.AppHost
// Starts Postgres and Redis containers, both mocks, the API and the Blazor UI on the same ports as Docker Compose
// (API 8080, mocks 8081/8082, UI 5173), so stop the compose stack first. The dashboard shows each resource's console
// logs, the API's traces and its metrics.
var builder = DistributedApplication.CreateBuilder(args);

// Same image versions as docker-compose.yml and the Testcontainers tests (D5.5), not Aspire's newer defaults.
var postgres = builder.AddPostgres("postgres").WithImageTag("16-alpine").AddDatabase("docviewer");
var redis = builder.AddRedis("redis").WithImageTag("7-alpine");

var sales = builder.AddProject<Projects.SalesSystem_Mock>("sales-mock");
var service = builder.AddProject<Projects.ServiceSystem_Mock>("service-mock");

var api = builder.AddProject<Projects.DocViewer_Api>("api")
    // Same client timeouts as docker-compose.yml: the drivers' defaults wait 5–30 s on a dead dependency (E-018, E-019).
    .WithEnvironment("ConnectionStrings__DocViewer", $"{postgres.Resource};Timeout=3;Command Timeout=5")
    .WithEnvironment("ConnectionStrings__Redis", $"{redis.Resource},connectTimeout=2000,syncTimeout=1000,asyncTimeout=1000,abortConnect=false")
    .WithEnvironment("Persistence__Provider", "Postgres")
    .WithEnvironment("Sources__Sales__BaseUrl", sales.GetEndpoint("http"))
    .WithEnvironment("Sources__Service__BaseUrl", service.GetEndpoint("http"))
    // The dashboard only receives what is pushed over OTLP; metrics are Prometheus-only unless asked for.
    .WithEnvironment("OTEL_METRICS_EXPORTER", "otlp")
    .WithHttpHealthCheck("/readyz")
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(sales)
    .WaitFor(service);

builder.AddProject<Projects.DocViewer_Web>("web")
    .WaitFor(api);

builder.Build().Run();
