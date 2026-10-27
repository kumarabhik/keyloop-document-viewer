using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocViewer.Application.Persistence;
using DocViewer.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocViewer.Infrastructure.Persistence;

public sealed partial class EfSearchAuditWriter(
    IDbContextFactory<DocViewerDbContext> contextFactory,
    IOptions<PersistenceOptions> options,
    TimeProvider timeProvider,
    ILogger<EfSearchAuditWriter> logger) : ISearchAuditWriter
{
    public async Task WriteAsync(SearchAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            db.SearchAudits.Add(new SearchAuditEntity
            {
                TraceId = entry.TraceId,
                VinHash = HashVin(entry.VinInput, options.Value.VinHashSalt),
                Outcome = entry.Outcome.ToString().ToLowerInvariant(),
                CacheHit = entry.CacheHit,
                LatencyMs = (int)Math.Round(entry.Latency.TotalMilliseconds),
                SourceStatusJson = JsonSerializer.Serialize(
                    entry.SourceStatuses.ToDictionary(), PersistenceJsonContext.Default.DictionarySourceSystemSourceStatus),
                DocumentCount = entry.DocumentCount,
                CreatedAt = timeProvider.GetUtcNow(),
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailure(logger, entry.TraceId, ex);
        }
    }

    /// <summary>Salted SHA-256, lower-case hex. Lets us correlate searches for one VIN without storing it (D-011).</summary>
    internal static string HashVin(string vinInput, string salt) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(salt + vinInput.Trim().ToUpperInvariant())));

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "persistence.audit_failed traceId={TraceId}")]
    private static partial void LogFailure(ILogger logger, string traceId, Exception exception);
}
