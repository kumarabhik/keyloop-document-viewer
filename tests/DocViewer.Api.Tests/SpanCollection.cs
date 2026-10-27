using System.Collections;
using System.Diagnostics;

namespace DocViewer.Api.Tests;

/// <summary>
/// Thread-safe sink for the in-memory exporter. ActivitySource listeners are process-wide, so spans from
/// other test hosts running in parallel arrive here too (E-006); tests filter by their own trace ids.
/// </summary>
public sealed class SpanCollection : ICollection<Activity>
{
    private readonly List<Activity> _spans = [];
    private readonly Lock _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _spans.Count;
            }
        }
    }

    public bool IsReadOnly => false;

    public IReadOnlyList<Activity> ForTraces(params ActivityTraceId[] traceIds)
    {
        lock (_lock)
        {
            return [.. _spans.Where(s => traceIds.Contains(s.TraceId))];
        }
    }

    public void Add(Activity item)
    {
        lock (_lock)
        {
            _spans.Add(item);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _spans.Clear();
        }
    }

    public bool Contains(Activity item)
    {
        lock (_lock)
        {
            return _spans.Contains(item);
        }
    }

    public void CopyTo(Activity[] array, int arrayIndex)
    {
        lock (_lock)
        {
            _spans.CopyTo(array, arrayIndex);
        }
    }

    public bool Remove(Activity item)
    {
        lock (_lock)
        {
            return _spans.Remove(item);
        }
    }

    public IEnumerator<Activity> GetEnumerator()
    {
        lock (_lock)
        {
            return _spans.ToList().GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
