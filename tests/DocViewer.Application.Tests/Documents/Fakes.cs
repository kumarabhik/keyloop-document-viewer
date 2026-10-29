using System.Collections.Concurrent;
using System.Threading.Channels;
using DocViewer.Application.Documents;
using DocViewer.Application.Persistence;
using DocViewer.Application.Sources;
using DocViewer.Domain;

namespace DocViewer.Application.Tests.Documents;

internal static class TestData
{
    public static readonly Vin TestVin = Vin.Parse("MA3EWB81S00100001");

    public static Document Doc(string sourceId, SourceSystem source, string date = "2025-03-14", string? expiry = null) =>
        new()
        {
            SourceId = sourceId,
            Source = source,
            Vin = TestVin,
            Title = sourceId,
            Type = DocumentType.Insurance,
            Date = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
            ExpiryDate = expiry is null ? null : DateOnly.Parse(expiry, System.Globalization.CultureInfo.InvariantCulture),
        };
}

/// <summary>Adapter whose behaviour each test scripts. Counts calls and can be held open with a gate.</summary>
internal sealed class FakeAdapter(SourceSystem source, Func<Vin, Task<SourceResult>> behaviour) : ISourceAdapter
{
    private int _calls;

    public SourceSystem Source => source;

    public int Calls => Volatile.Read(ref _calls);

    public Task<SourceResult> GetDocumentsAsync(Vin vin, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return behaviour(vin);
    }

    public static FakeAdapter Returning(SourceSystem source, params Document[] documents) =>
        new(source, _ => Task.FromResult(SourceResult.Ok(source, documents, TimeSpan.FromMilliseconds(10))));

    public static FakeAdapter Failing(SourceSystem source, SourceStatus status = SourceStatus.Error, string error = "http_500") =>
        new(source, _ => Task.FromResult(SourceResult.Failed(source, status, error, TimeSpan.FromMilliseconds(10))));
}

internal sealed class FakeCache : IDocumentCache
{
    public ConcurrentDictionary<Vin, AggregatedDocuments> Entries { get; } = new();

    private int _sets;

    public int Sets => Volatile.Read(ref _sets);

    /// <summary>Calls never answer and ignore cancellation, like a stopped Redis (E-019).</summary>
    public bool Hangs { get; set; }

    public Task<AggregatedDocuments?> GetAsync(Vin vin, CancellationToken cancellationToken) =>
        Hangs ? new TaskCompletionSource<AggregatedDocuments?>().Task : Task.FromResult(Entries.GetValueOrDefault(vin));

    public Task SetAsync(AggregatedDocuments documents, CancellationToken cancellationToken)
    {
        if (Hangs)
        {
            return new TaskCompletionSource().Task;
        }

        Interlocked.Increment(ref _sets);
        Entries[documents.Vin] = documents;
        return Task.CompletedTask;
    }
}

internal sealed class FakeSnapshots : ISnapshotRepository
{
    private readonly Channel<SourceSystem> _saved = Channel.CreateUnbounded<SourceSystem>();

    public ConcurrentDictionary<(Vin, SourceSystem), SourceSnapshot> Stored { get; } = new();

    /// <summary>Reads never answer and ignore cancellation, like a stopped Postgres container (E-018).</summary>
    public bool ReadsHang { get; set; }

    public Task<SourceSnapshot?> GetAsync(Vin vin, SourceSystem source, CancellationToken cancellationToken) =>
        ReadsHang ? new TaskCompletionSource<SourceSnapshot?>().Task : Task.FromResult(Stored.GetValueOrDefault((vin, source)));

    public Task SaveAsync(Vin vin, SourceSystem source, IReadOnlyList<Document> documents, CancellationToken cancellationToken)
    {
        Stored[(vin, source)] = new SourceSnapshot(source, documents, DateTimeOffset.UnixEpoch);
        _saved.Writer.TryWrite(source);
        return Task.CompletedTask;
    }

    public async Task WaitForSavesAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await _saved.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}

internal sealed class FakeAudit : ISearchAuditWriter
{
    private readonly Channel<SearchAuditEntry> _written = Channel.CreateUnbounded<SearchAuditEntry>();

    public ConcurrentQueue<SearchAuditEntry> Entries { get; } = new();

    public Func<Task>? OnWrite { get; set; }

    public async Task WriteAsync(SearchAuditEntry entry, CancellationToken cancellationToken)
    {
        Entries.Enqueue(entry);
        _written.Writer.TryWrite(entry);
        if (OnWrite is not null)
        {
            await OnWrite();
        }
    }

    public async Task<SearchAuditEntry> WaitForEntryAsync()
    {
        return await _written.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
}
