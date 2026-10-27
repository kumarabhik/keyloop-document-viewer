using DocViewer.Application.Persistence;
using DocViewer.Application.Sources;
using DocViewer.Domain;
using DocViewer.Infrastructure.Options;
using DocViewer.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace DocViewer.Infrastructure.Tests.Persistence;

public sealed class EfSearchAuditWriterTests : IDisposable
{
    private const string Salt = "test-salt-123";
    private const string RawVin = "MA3EWB81S00100001";

    private readonly SqliteTestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly EfSearchAuditWriter _writer;

    public EfSearchAuditWriterTests() =>
        _writer = new EfSearchAuditWriter(
            _db,
            MsOptions.Create(new PersistenceOptions { VinHashSalt = Salt }),
            _time,
            NullLogger<EfSearchAuditWriter>.Instance);

    public void Dispose() => _db.Dispose();

    private static SearchAuditEntry Entry(string vin = RawVin) => new(
        TraceId: "4bf92f3577b34da6a3ce929d0e0e4736",
        VinInput: vin,
        Outcome: SearchOutcome.Partial,
        CacheHit: false,
        Latency: TimeSpan.FromMilliseconds(2003.4),
        SourceStatuses: new Dictionary<SourceSystem, SourceStatus>
        {
            [SourceSystem.Sales] = SourceStatus.Ok,
            [SourceSystem.Service] = SourceStatus.Timeout,
        },
        DocumentCount: 4);

    [Fact]
    public async Task Write_PersistsOneRow()
    {
        await _writer.WriteAsync(Entry(), CancellationToken.None);

        await using var db = _db.CreateDbContext();
        var row = db.SearchAudits.Single();
        row.TraceId.ShouldBe("4bf92f3577b34da6a3ce929d0e0e4736");
        row.Outcome.ShouldBe("partial");
        row.CacheHit.ShouldBeFalse();
        row.LatencyMs.ShouldBe(2003);
        row.DocumentCount.ShouldBe(4);
        row.CreatedAt.ShouldBe(_time.GetUtcNow());
        row.SourceStatusJson.ShouldContain("\"Service\":\"Timeout\"");
    }

    [Fact]
    [Trait("Invariant", "7")]
    public async Task Write_StoresSaltedHash_NeverTheRawVin()
    {
        await _writer.WriteAsync(Entry(), CancellationToken.None);

        await using var db = _db.CreateDbContext();
        var row = db.SearchAudits.Single();
        row.VinHash.Length.ShouldBe(64);
        row.VinHash.ShouldBe(EfSearchAuditWriter.HashVin(RawVin, Salt));
        row.VinHash.ShouldNotContain(RawVin);
        row.SourceStatusJson.ShouldNotContain(RawVin);
    }

    [Fact]
    public void HashVin_IsStableAcrossCaseAndWhitespace_AndDependsOnSalt()
    {
        EfSearchAuditWriter.HashVin(" ma3ewb81s00100001 ", Salt).ShouldBe(EfSearchAuditWriter.HashVin(RawVin, Salt));
        EfSearchAuditWriter.HashVin(RawVin, "other-salt-456").ShouldNotBe(EfSearchAuditWriter.HashVin(RawVin, Salt));
    }

    [Fact]
    [Trait("Invariant", "10")]
    public async Task DatabaseOutage_IsSwallowed()
    {
        _db.Break();

        await Should.NotThrowAsync(() => _writer.WriteAsync(Entry(), CancellationToken.None));
    }
}
