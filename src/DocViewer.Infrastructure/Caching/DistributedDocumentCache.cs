using System.Text.Json;
using DocViewer.Application.Documents;
using DocViewer.Application.Observability;
using DocViewer.Application.Options;
using DocViewer.Domain;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocViewer.Infrastructure.Caching;

/// <summary>
/// <see cref="IDocumentCache"/> over <see cref="IDistributedCache"/> — in-memory or Redis (D-015).
/// Any cache failure is logged and treated as a miss so a Redis outage never fails a search.
/// </summary>
public sealed partial class DistributedDocumentCache(
    IDistributedCache cache,
    IOptions<CacheOptions> options,
    DocViewerMetrics metrics,
    ILogger<DistributedDocumentCache> logger) : IDocumentCache
{
    // Version prefix: bump when the cached shape changes so old entries are simply ignored.
    internal static string KeyFor(Vin vin) => $"docs:v1:{vin.Value}";

    public async Task<AggregatedDocuments?> GetAsync(Vin vin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vin);
        try
        {
            var bytes = await cache.GetAsync(KeyFor(vin), cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize(bytes, CacheJsonContext.Default.AggregatedDocuments);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCacheFailure(logger, "get", vin.Masked, ex);
            metrics.CacheLookup("error");
            return null;
        }
    }

    public async Task SetAsync(AggregatedDocuments documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Partial)
        {
            // Defence in depth for D-006: the aggregator should never ask, but never store a partial result.
            return;
        }

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(documents, CacheJsonContext.Default.AggregatedDocuments);
            await cache.SetAsync(
                KeyFor(documents.Vin),
                bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.Value.Ttl },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCacheFailure(logger, "set", documents.Vin.Masked, ex);
            metrics.CacheLookup("error");
        }
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Warning, Message = "cache.failed operation={Operation} vin={VinMasked}")]
    private static partial void LogCacheFailure(ILogger logger, string operation, string vinMasked, Exception exception);
}
