using System.Text.Json;
using DocViewer.Application.Persistence;
using DocViewer.Domain;
using DocViewer.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocViewer.Infrastructure.Persistence;

public sealed partial class EfSnapshotRepository(
    IDbContextFactory<DocViewerDbContext> contextFactory,
    IOptions<PersistenceOptions> options,
    TimeProvider timeProvider,
    ILogger<EfSnapshotRepository> logger) : ISnapshotRepository
{
    public async Task<SourceSnapshot?> GetAsync(Vin vin, SourceSystem source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vin);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.SourceSnapshots.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Vin == vin.Value && s.Source == source.ToString(), cancellationToken);

            // Age is checked in memory: SQLite cannot translate DateTimeOffset comparisons, and this is a PK lookup anyway.
            if (row is null || timeProvider.GetUtcNow() - row.FetchedAt > options.Value.SnapshotMaxAge)
            {
                return null;
            }

            var documents = JsonSerializer.Deserialize(row.DocumentsJson, PersistenceJsonContext.Default.ListDocument) ?? [];
            return new SourceSnapshot(source, documents, row.FetchedAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailure(logger, "snapshot.get", vin.Masked, ex);
            return null;
        }
    }

    public async Task SaveAsync(Vin vin, SourceSystem source, IReadOnlyList<Document> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vin);
        ArgumentNullException.ThrowIfNull(documents);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var json = JsonSerializer.Serialize(documents.ToList(), PersistenceJsonContext.Default.ListDocument);
            var key = source.ToString();

            var row = await db.SourceSnapshots.FindAsync([vin.Value, key], cancellationToken);
            if (row is null)
            {
                db.SourceSnapshots.Add(new SourceSnapshotEntity
                {
                    Vin = vin.Value,
                    Source = key,
                    DocumentsJson = json,
                    FetchedAt = timeProvider.GetUtcNow(),
                });
            }
            else
            {
                row.DocumentsJson = json;
                row.FetchedAt = timeProvider.GetUtcNow();
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Two concurrent first-time saves can race on the insert; losing one is harmless (next search re-saves).
            LogFailure(logger, "snapshot.save", vin.Masked, ex);
        }
    }

    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning, Message = "persistence.failed operation={Operation} vin={VinMasked}")]
    private static partial void LogFailure(ILogger logger, string operation, string vinMasked, Exception exception);
}
