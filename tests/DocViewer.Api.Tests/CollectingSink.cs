using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace DocViewer.Api.Tests;

/// <summary>Captures every Serilog event the API writes, exactly as the production JSON formatter renders it.</summary>
public sealed class CollectingSink : ILogEventSink
{
    private static readonly RenderedCompactJsonFormatter Formatter = new();

    public ConcurrentQueue<LogEvent> Events { get; } = new();

    public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);

    public static string Render(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        Formatter.Format(logEvent, writer);
        return writer.ToString();
    }
}
