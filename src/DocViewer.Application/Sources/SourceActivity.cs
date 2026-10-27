using System.Collections.Concurrent;
using DocViewer.Domain;

namespace DocViewer.Application.Sources;

public sealed record SourceObservation(SourceStatus Status, TimeSpan Latency, DateTimeOffset At, string? Error);

/// <summary>Last live outcome per source in this process, for <c>/api/v1/sources/health</c>.</summary>
public sealed class SourceActivity
{
    private readonly ConcurrentDictionary<SourceSystem, SourceObservation> _last = new();

    public void Record(SourceResult result, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(result);
        _last[result.Source] = new SourceObservation(result.Status, result.Latency, at, result.Error);
    }

    public SourceObservation? LastFor(SourceSystem source) => _last.GetValueOrDefault(source);
}
