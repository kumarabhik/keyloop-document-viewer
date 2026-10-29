using System.Collections.Concurrent;
using System.Diagnostics;
using DocViewer.Application.Observability;
using DocViewer.Application.Options;
using DocViewer.Application.Persistence;
using DocViewer.Application.Sources;
using DocViewer.Application.Time;
using DocViewer.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocViewer.Application.Documents;

/// <summary>
/// Orchestrates one VIN search (DESIGN.md §6.2): cache → parallel fan-out → stale fallback →
/// merge → cache/persist. Knows nothing about any source's payload format.
/// </summary>
public sealed partial class AggregationService(
    IServiceScopeFactory scopeFactory,
    IDocumentCache cache,
    ISnapshotRepository snapshots,
    ISearchAuditWriter audit,
    SourceActivity activity,
    DocViewerMetrics metrics,
    DealershipCalendar calendar,
    IOptions<BusinessRulesOptions> rules,
    IOptions<LatencyBudgetOptions> budget,
    TimeProvider timeProvider,
    ILogger<AggregationService> logger)
{
    /// <summary>Upper bound for best-effort writes that run after the response is built.</summary>
    internal static readonly TimeSpan BackgroundWriteTimeout = TimeSpan.FromSeconds(5);

    // Single-flight (D-020): concurrent misses for one VIN share one fan-out per process.
    private readonly ConcurrentDictionary<Vin, Lazy<Task<AggregatedDocuments>>> _inFlight = new();

    public async Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = timeProvider.GetTimestamp();
        using var span = DocViewerTracing.Source.StartActivity("aggregate");

        if (!request.Refresh)
        {
            AggregatedDocuments? cached;
            using (DocViewerTracing.Source.StartActivity("cache.get"))
            {
                cached = await WithinBudgetAsync(() => cache.GetAsync(request.Vin, cancellationToken), "cache.get", request.Vin);
            }

            metrics.CacheLookup(cached is null ? "miss" : "hit");
            if (cached is not null)
            {
                return Finish(request, new SearchResult(cached, CacheStatus.Hit), started, span);
            }
        }

        // The shared fan-out is not tied to this caller's token: another caller may be waiting on it,
        // and the per-source budgets (D-005) already bound it to ~2.2 s. This caller can still give up.
        var documents = await FanOutOnceAsync(request.Vin).WaitAsync(cancellationToken);
        var cacheStatus = request.Refresh ? CacheStatus.Bypass : CacheStatus.Miss;
        if (request.Refresh)
        {
            metrics.CacheLookup("bypass");
        }

        return Finish(request, new SearchResult(documents, cacheStatus), started, span);
    }

    /// <summary>Audits a search rejected before any downstream call (invalid VIN).</summary>
    public void RecordInvalidSearch(string vinInput, string traceId)
    {
        var entry = new SearchAuditEntry(traceId, vinInput ?? string.Empty, SearchOutcome.Invalid, false, TimeSpan.Zero,
            new Dictionary<SourceSystem, SourceStatus>(), 0);
        RunInBackground(ct => audit.WriteAsync(entry, ct), "audit.write");
    }

    private Task<AggregatedDocuments> FanOutOnceAsync(Vin vin) =>
        _inFlight.GetOrAdd(vin, v => new Lazy<Task<AggregatedDocuments>>(() => AggregateAndReleaseAsync(v))).Value;

    private async Task<AggregatedDocuments> AggregateAndReleaseAsync(Vin vin)
    {
        try
        {
            return await AggregateAsync(vin);
        }
        finally
        {
            _inFlight.TryRemove(vin, out _);
        }
    }

    private async Task<AggregatedDocuments> AggregateAsync(Vin vin)
    {
        // Typed HttpClients are transient; this fan-out may outlive the request that started it,
        // so it resolves adapters in its own scope rather than borrowing the caller's.
        await using var scope = scopeFactory.CreateAsyncScope();
        var adapters = scope.ServiceProvider.GetServices<ISourceAdapter>();
        var live = await Task.WhenAll(adapters.Select(a => CallSafelyAsync(a, vin)));
        foreach (var result in live)
        {
            activity.Record(result, timeProvider.GetUtcNow());
            metrics.SourceCalled(result);
        }

        var sources = await Task.WhenAll(live.Select(r => r.IsOk ? Task.FromResult(r) : WithFallbackAsync(vin, r)));

        IReadOnlyList<Document> merged;
        using (DocViewerTracing.Source.StartActivity("merge"))
        {
            merged = DocumentMerger.Merge(sources.SelectMany(s => s.Documents), calendar.Today, rules.Value.ExpiryWarningDays);
        }

        var aggregated = new AggregatedDocuments
        {
            Vin = vin,
            Sources = [.. sources.OrderBy(s => s.Source)],
            Documents = merged,
            GeneratedAt = timeProvider.GetUtcNow(),
        };

        foreach (var failed in aggregated.Sources.Where(r => !r.IsOk))
        {
            metrics.PartialResponse(failed.Source);
            if (failed.Status == SourceStatus.Stale)
            {
                metrics.StaleServed(failed.Source);
            }
        }

        if (!aggregated.Partial)
        {
            // D-006. Budgeted (E-019): a stuck write would otherwise hold this shared fan-out and every waiting caller.
            await WithinBudgetAsync(() => SetCacheAsync(aggregated), "cache.set", vin);
        }

        foreach (var ok in live.Where(r => r.IsOk))
        {
            RunInBackground(ct => snapshots.SaveAsync(vin, ok.Source, ok.Documents, ct), "snapshot.persist");
        }

        return aggregated;
    }

    /// <summary>
    /// Starts <paramref name="call"/> on the thread pool, so work it does synchronously before returning its task
    /// counts against the budget too: the first Redis call connected synchronously for ~1.5 s, unseen by
    /// <c>WaitAsync</c> (E-019).
    /// </summary>
    private static Task<T> OffThread<T>(Func<Task<T>> call) => Task.Run(call);

    private async Task<bool> SetCacheAsync(AggregatedDocuments aggregated)
    {
        await cache.SetAsync(aggregated, CancellationToken.None);
        return true;
    }

    /// <summary>
    /// Waits for a cache call at most <see cref="LatencyBudgetOptions.CacheTimeout"/>, then carries on as a miss.
    /// <c>IDocumentCache</c> already turns errors into misses; this covers a cache that hangs (E-019, invariant 5).
    /// </summary>
    private async Task<T?> WithinBudgetAsync<T>(Func<Task<T?>> call, string operation, Vin vin)
    {
        try
        {
            return await OffThread(call).WaitAsync(budget.Value.CacheTimeout, timeProvider);
        }
        catch (TimeoutException)
        {
            metrics.CacheLookup("timeout");
            LogCacheTimedOut(logger, operation, vin.Masked, budget.Value.CacheTimeoutMs);
            return default;
        }
    }

    /// <summary>Adapters must not throw (AGENTS.md §4.3); this guard keeps invariant 1 even if one has a bug.</summary>
    private async Task<SourceResult> CallSafelyAsync(ISourceAdapter adapter, Vin vin)
    {
        var started = timeProvider.GetTimestamp();
        using var span = DocViewerTracing.Source.StartActivity($"source.{adapter.Source.ToString().ToLowerInvariant()}");
        SourceResult result;
        try
        {
            result = await adapter.GetDocumentsAsync(vin, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogAdapterThrew(logger, adapter.Source, vin.Masked, ex);
            result = SourceResult.Failed(adapter.Source, SourceStatus.Error, "unexpected", timeProvider.GetElapsedTime(started));
        }

        span?.SetTag("docviewer.source.status", result.Status.ToString())
            .SetTag("docviewer.document_count", result.Documents.Count);
        if (!result.IsOk)
        {
            span?.SetStatus(ActivityStatusCode.Error, result.Error);
        }

        return result;
    }

    /// <summary>
    /// Stands in the last-known-good snapshot for a failed source, keeping the original error (D-003). The read gets its
    /// own small budget: it runs after the source has used its 2 s, and a stopped database does not always honour
    /// cancellation, so we stop waiting ourselves (E-018, invariant 5).
    /// </summary>
    private async Task<SourceResult> WithFallbackAsync(Vin vin, SourceResult failed)
    {
        SourceSnapshot? snapshot;
        try
        {
            snapshot = await OffThread(() => snapshots.GetAsync(vin, failed.Source, CancellationToken.None))
                .WaitAsync(budget.Value.SnapshotReadTimeout, timeProvider);
        }
        catch (TimeoutException)
        {
            LogSnapshotReadTimedOut(logger, failed.Source, vin.Masked, budget.Value.SnapshotReadTimeoutMs);
            return failed;
        }

        return snapshot is null
            ? failed
            : failed with { Status = SourceStatus.Stale, Documents = snapshot.Documents, AsOf = snapshot.FetchedAt };
    }

    private SearchResult Finish(SearchRequest request, SearchResult result, long started, Activity? span)
    {
        span?.SetTag("docviewer.outcome", result.Outcome.ToString())
            .SetTag("docviewer.cache", result.Cache.ToString())
            .SetTag("docviewer.document_count", result.Documents.Documents.Count);

        var latency = timeProvider.GetElapsedTime(started);
        var documents = result.Documents;
        LogSearchCompleted(logger, request.Vin.Masked, result.Outcome, result.Cache, documents.Documents.Count, latency.TotalMilliseconds);

        var entry = new SearchAuditEntry(
            request.TraceId,
            request.Vin.Value,
            result.Outcome,
            result.Cache == CacheStatus.Hit,
            latency,
            documents.Sources.ToDictionary(s => s.Source, s => s.Status),
            documents.Documents.Count);
        RunInBackground(ct => audit.WriteAsync(entry, ct), "audit.write");

        return result;
    }

    /// <summary>Persistence is off the critical path: a slow or broken database never delays a response (invariant 10).</summary>
    private void RunInBackground(Func<CancellationToken, Task> work, string operation) =>
        _ = Task.Run(async () =>
        {
            using var span = DocViewerTracing.Source.StartActivity(operation);
            using var timeout = new CancellationTokenSource(BackgroundWriteTimeout, timeProvider);
            try
            {
                await work(timeout.Token);
            }
            catch (Exception ex)
            {
                LogBackgroundWriteFailed(logger, operation, ex);
            }
        });

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "search.completed vin={VinMasked} outcome={Outcome} cache={Cache} documents={DocumentCount} latencyMs={LatencyMs}")]
    private static partial void LogSearchCompleted(ILogger logger, string vinMasked, SearchOutcome outcome, CacheStatus cache, int documentCount, double latencyMs);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "source.adapter_threw {Source} vin={VinMasked}")]
    private static partial void LogAdapterThrew(ILogger logger, SourceSystem source, string vinMasked, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "snapshot.read_timed_out {Source} vin={VinMasked} budgetMs={BudgetMs}")]
    private static partial void LogSnapshotReadTimedOut(ILogger logger, SourceSystem source, string vinMasked, int budgetMs);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "cache.timed_out operation={Operation} vin={VinMasked} budgetMs={BudgetMs}")]
    private static partial void LogCacheTimedOut(ILogger logger, string operation, string vinMasked, int budgetMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "persistence.background_failed operation={Operation}")]
    private static partial void LogBackgroundWriteFailed(ILogger logger, string operation, Exception exception);
}
