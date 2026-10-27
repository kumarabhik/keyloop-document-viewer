using Serilog.Core;
using Serilog.Events;

namespace DocViewer.Api.Observability;

/// <summary>
/// ASP.NET Core opens a logging scope per request with <c>RequestPath</c>, and Serilog copies scope
/// properties onto every event in that request. Our paths contain the VIN, so the property is dropped
/// (invariant 7, E-007). The access log records the route template instead, and the trace id correlates events.
/// </summary>
internal sealed class DropRequestPathEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
        logEvent.RemovePropertyIfPresent("RequestPath");
}
