using DocViewer.Api;
using DocViewer.Api.Contracts;
using DocViewer.Api.Endpoints;
using DocViewer.Api.Http;
using DocViewer.Api.Observability;
using DocViewer.Application;
using DocViewer.Infrastructure;
using DocViewer.Infrastructure.Options;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

if (ContainerHealthCheck.IsRequested(args))
{
    return await ContainerHealthCheck.RunAsync(args); // Docker HEALTHCHECK; never starts the app
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.UseBuildTimePlaceholders();
builder.AddStructuredLogging();
builder.AddTelemetry();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(o => ApiJson.Configure(o.SerializerOptions));
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
    c.ProblemDetails.Extensions["traceId"] = TraceIds.Current(c.HttpContext));

builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Unified Document Viewer API";
    document.Info.Version = "v1";
    document.Info.Description =
        "One VIN search across the dealership's Sales and Service systems. Every document names its source; " +
        "a failing source makes the response partial (200), never an error.";
    return Task.CompletedTask;
}).AddOperationTransformer<DocumentParametersTransformer>().AddSchemaTransformer<OptionalWhenNullSchemaTransformer>());

builder.Services.AddSearchRateLimiting();
if (!StartupTasks.IsGeneratingOpenApiDocument && builder.Configuration.GetValue("Refresh:Enabled", true))
{
    builder.Services.AddHostedService<RefreshWorker>(); // D-033
}
builder.Services.AddSingleton<IValidateOptions<PersistenceOptions>, ProductionSaltValidator>(); // D-029

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(
    DatabaseHealthCheck.Name, failureStatus: HealthStatus.Degraded, tags: ["ready"], timeout: DatabaseHealthCheck.Timeout);

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(corsOrigins)
    .WithMethods("GET")
    .AllowAnyHeader()
    .WithExposedHeaders(TraceIds.HeaderName, DocumentEndpoints.CacheHeader, "Retry-After")));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseTraceIdHeader();
app.UseAccessLog();
app.UseCors();
app.UseRateLimiter(); // after the trace id, so a 429 problem carries one too

app.MapDocumentEndpoints();
app.MapOpenApi(); // GET /openapi/v1.json
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(); // GET /scalar
}

app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false }); // liveness: process is up
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapPrometheusScrapingEndpoint(); // GET /metrics

await StartupTasks.InitializeDatabaseAsync(app);
await app.RunAsync();
return 0;
