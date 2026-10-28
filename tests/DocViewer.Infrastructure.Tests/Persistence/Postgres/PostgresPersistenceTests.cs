using System.Text.Json;
using DocViewer.Application.Persistence;
using DocViewer.Application.Sources;
using DocViewer.Domain;
using DocViewer.Infrastructure.Options;
using DocViewer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace DocViewer.Infrastructure.Tests.Persistence.Postgres;

/// <summary>
/// D5.5: the persistence tests again, on the real database Docker runs (D-019), to catch SQLite/PostgreSQL
/// differences: migrations, <c>jsonb</c> columns, <c>timestamptz</c> round-trips and upserts.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class PostgresPersistenceTests(PostgresContainer postgres) : IAsyncLifetime
{
    private const string Salt = "test-salt-123";
    private static readonly Vin Vin = Vin.Parse("MA3EWB81S00100001");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        if (new PostgresFactAttribute().Skip is null)
        {
            await postgres.ResetAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private EfSnapshotRepository Snapshots() => new(
        postgres,
        MsOptions.Create(new PersistenceOptions { SnapshotMaxAgeHours = 24, VinHashSalt = Salt }),
        _time,
        NullLogger<EfSnapshotRepository>.Instance);

    private EfSearchAuditWriter Audit() => new(
        postgres, MsOptions.Create(new PersistenceOptions { VinHashSalt = Salt }), _time, NullLogger<EfSearchAuditWriter>.Instance);

    private static Document Doc(string id, SourceSystem source = SourceSystem.Service, string title = "Doc") => new()
    {
        SourceId = id,
        Source = source,
        Vin = Vin,
        Title = title,
        Type = DocumentType.JobCard,
        Date = new DateOnly(2025, 5, 1),
        ExpiryDate = new DateOnly(2027, 1, 31),
        Metadata = new Dictionary<string, string> { ["workshop"] = "Koramangala", ["note"] = "says \"urgent\" — ₹ 4,500" },
    };

    [PostgresFact]
    public async Task Migrations_AreAllApplied_AndJsonColumnsAreJsonb()
    {
        await using var db = postgres.CreateDbContext();

        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await ColumnTypeAsync(db, "source_snapshot", "documents")).ShouldBe("jsonb");
        (await ColumnTypeAsync(db, "search_audit", "source_status")).ShouldBe("jsonb");
        (await ColumnTypeAsync(db, "source_snapshot", "fetched_at")).ShouldBe("timestamp with time zone");
    }

    [PostgresFact]
    public async Task Snapshot_RoundTripsThroughJsonb_KeepingOrderDatesAndText()
    {
        await Snapshots().SaveAsync(Vin, SourceSystem.Service, [Doc("SV-JC-2", title: "Zweite"), Doc("SV-JC-1", title: "First")], CancellationToken.None);

        var snapshot = await Snapshots().GetAsync(Vin, SourceSystem.Service, CancellationToken.None);

        snapshot.ShouldNotBeNull();
        snapshot.FetchedAt.ShouldBe(_time.GetUtcNow());
        snapshot.Documents.Select(d => d.SourceId).ShouldBe(["SV-JC-2", "SV-JC-1"]); // jsonb keeps array order
        snapshot.Documents[0].ExpiryDate.ShouldBe(new DateOnly(2027, 1, 31));
        snapshot.Documents[0].Metadata["note"].ShouldBe("says \"urgent\" — ₹ 4,500");
    }

    [PostgresFact]
    public async Task Snapshot_SavedTwice_IsOneRowWithTheNewestData()
    {
        await Snapshots().SaveAsync(Vin, SourceSystem.Service, [Doc("OLD")], CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(1));
        await Snapshots().SaveAsync(Vin, SourceSystem.Service, [Doc("NEW")], CancellationToken.None);

        var snapshot = await Snapshots().GetAsync(Vin, SourceSystem.Service, CancellationToken.None);
        snapshot!.Documents.Select(d => d.SourceId).ShouldBe(["NEW"]);
        snapshot.FetchedAt.ShouldBe(_time.GetUtcNow());
        await using var db = postgres.CreateDbContext();
        (await db.SourceSnapshots.CountAsync()).ShouldBe(1);
    }

    [PostgresFact]
    public async Task Snapshots_AreKeptPerSource()
    {
        await Snapshots().SaveAsync(Vin, SourceSystem.Sales, [Doc("S", SourceSystem.Sales)], CancellationToken.None);
        await Snapshots().SaveAsync(Vin, SourceSystem.Service, [Doc("V")], CancellationToken.None);

        (await Snapshots().GetAsync(Vin, SourceSystem.Sales, CancellationToken.None))!.Documents.Single().SourceId.ShouldBe("S");
        (await Snapshots().GetAsync(Vin, SourceSystem.Service, CancellationToken.None))!.Documents.Single().SourceId.ShouldBe("V");
    }

    [PostgresFact]
    public async Task Snapshot_MaxAge_IsInclusiveAt24Hours()
    {
        await Snapshots().SaveAsync(Vin, SourceSystem.Service, [Doc("X")], CancellationToken.None);

        _time.Advance(TimeSpan.FromHours(24));
        (await Snapshots().GetAsync(Vin, SourceSystem.Service, CancellationToken.None)).ShouldNotBeNull();

        _time.Advance(TimeSpan.FromMinutes(1));
        (await Snapshots().GetAsync(Vin, SourceSystem.Service, CancellationToken.None)).ShouldBeNull();
    }

    [PostgresFact]
    [Trait("Invariant", "7")]
    public async Task Audit_WritesOneRow_WithHashedVinAndReadableStatusJson()
    {
        await Audit().WriteAsync(new SearchAuditEntry(
            TraceId: "4bf92f3577b34da6a3ce929d0e0e4736",
            VinInput: Vin.Value,
            Outcome: SearchOutcome.Partial,
            CacheHit: false,
            Latency: TimeSpan.FromMilliseconds(2003.4),
            SourceStatuses: new Dictionary<SourceSystem, SourceStatus> { [SourceSystem.Sales] = SourceStatus.Ok, [SourceSystem.Service] = SourceStatus.Timeout },
            DocumentCount: 4), CancellationToken.None);

        await using var db = postgres.CreateDbContext();
        var row = await db.SearchAudits.SingleAsync();
        row.VinHash.ShouldBe(EfSearchAuditWriter.HashVin(Vin.Value, Salt));
        row.VinHash.ShouldNotContain(Vin.Value);
        row.Outcome.ShouldBe("partial");
        row.CreatedAt.ShouldBe(_time.GetUtcNow());

        // jsonb re-formats what it stores ("Service": "Timeout", with a space), unlike SQLite TEXT, so compare
        // the parsed value, not the text (see D-019).
        var statuses = JsonSerializer.Deserialize<Dictionary<string, string>>(row.SourceStatusJson)!;
        statuses.ShouldBe(new Dictionary<string, string> { ["Sales"] = "Ok", ["Service"] = "Timeout" }, ignoreOrder: true);
    }

    private static async Task<string> ColumnTypeAsync(DocViewerDbContext db, string table, string column) =>
        await db.Database
            .SqlQuery<string>($"SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_name = {table} AND column_name = {column}")
            .SingleAsync();
}
