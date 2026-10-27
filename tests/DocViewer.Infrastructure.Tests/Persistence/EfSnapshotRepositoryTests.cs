using DocViewer.Domain;
using DocViewer.Infrastructure.Options;
using DocViewer.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace DocViewer.Infrastructure.Tests.Persistence;

public sealed class EfSnapshotRepositoryTests : IDisposable
{
    private static readonly Vin Vin = Vin.Parse("MA3EWB81S00100001");

    private readonly SqliteTestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly EfSnapshotRepository _repository;

    public EfSnapshotRepositoryTests() =>
        _repository = new EfSnapshotRepository(
            _db,
            MsOptions.Create(new PersistenceOptions { SnapshotMaxAgeHours = 24, VinHashSalt = "test-salt-123" }),
            _time,
            NullLogger<EfSnapshotRepository>.Instance);

    public void Dispose() => _db.Dispose();

    private static Document Doc(string id, SourceSystem source = SourceSystem.Service) => new()
    {
        SourceId = id,
        Source = source,
        Vin = Vin,
        Title = $"Doc {id}",
        Type = DocumentType.JobCard,
        Date = new DateOnly(2025, 5, 1),
        Metadata = new Dictionary<string, string> { ["workshop"] = "Koramangala" },
    };

    [Fact]
    public async Task SaveThenGet_ReturnsDocumentsAndFetchTime()
    {
        await _repository.SaveAsync(Vin, SourceSystem.Service, [Doc("SV-JC-1"), Doc("SV-JC-2")], CancellationToken.None);

        var snapshot = await _repository.GetAsync(Vin, SourceSystem.Service, CancellationToken.None);

        snapshot.ShouldNotBeNull();
        snapshot.Source.ShouldBe(SourceSystem.Service);
        snapshot.FetchedAt.ShouldBe(_time.GetUtcNow());
        snapshot.Documents.Select(d => d.Id).ShouldBe(["SERVICE:SV-JC-1", "SERVICE:SV-JC-2"]);
        snapshot.Documents[0].Metadata["workshop"].ShouldBe("Koramangala");
    }

    [Fact]
    public async Task Save_Twice_OverwritesAndRefreshesTimestamp()
    {
        await _repository.SaveAsync(Vin, SourceSystem.Service, [Doc("OLD")], CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(1));

        await _repository.SaveAsync(Vin, SourceSystem.Service, [Doc("NEW")], CancellationToken.None);
        var snapshot = await _repository.GetAsync(Vin, SourceSystem.Service, CancellationToken.None);

        snapshot!.Documents.Select(d => d.SourceId).ShouldBe(["NEW"]);
        snapshot.FetchedAt.ShouldBe(_time.GetUtcNow());
        await using var db = _db.CreateDbContext();
        db.SourceSnapshots.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Snapshots_AreKeptPerSource()
    {
        await _repository.SaveAsync(Vin, SourceSystem.Sales, [Doc("S", SourceSystem.Sales)], CancellationToken.None);
        await _repository.SaveAsync(Vin, SourceSystem.Service, [Doc("V")], CancellationToken.None);

        (await _repository.GetAsync(Vin, SourceSystem.Sales, CancellationToken.None))!.Documents.Single().SourceId.ShouldBe("S");
        (await _repository.GetAsync(Vin, SourceSystem.Service, CancellationToken.None))!.Documents.Single().SourceId.ShouldBe("V");
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
    {
        (await _repository.GetAsync(Vin, SourceSystem.Sales, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Get_SnapshotAtMaxAge_IsStillServed()
    {
        await _repository.SaveAsync(Vin, SourceSystem.Service, [Doc("X")], CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(24));

        (await _repository.GetAsync(Vin, SourceSystem.Service, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Get_SnapshotOlderThanMaxAge_IsIgnored()
    {
        await _repository.SaveAsync(Vin, SourceSystem.Service, [Doc("X")], CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));

        (await _repository.GetAsync(Vin, SourceSystem.Service, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("Invariant", "10")]
    public async Task DatabaseOutage_GetReturnsNull_SaveIsNoOp()
    {
        _db.Break();

        (await _repository.GetAsync(Vin, SourceSystem.Service, CancellationToken.None)).ShouldBeNull();
        await Should.NotThrowAsync(() => _repository.SaveAsync(Vin, SourceSystem.Service, [Doc("X")], CancellationToken.None));
    }
}
