using DocViewer.Application.Documents;
using DocViewer.Application.Options;
using DocViewer.Application.Sources;
using DocViewer.Domain;
using DocViewer.Infrastructure.Caching;
using DocViewer.Infrastructure.Tests.Observability;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace DocViewer.Infrastructure.Tests.Caching;

public sealed class DistributedDocumentCacheTests : IDisposable
{
    private readonly TestMetrics _metrics = new();

    public void Dispose() => _metrics.Dispose();

    private static readonly Vin Vin = Vin.Parse("MA3EWB81S00100006");

    private DistributedDocumentCache Create(IDistributedCache cache, int ttlSeconds = 60) =>
        new(cache, MsOptions.Create(new CacheOptions { TtlSeconds = ttlSeconds }), _metrics.Metrics, NullLogger<DistributedDocumentCache>.Instance);

    private static MemoryDistributedCache MemoryCache() => new(MsOptions.Create(new MemoryDistributedCacheOptions()));

    private static AggregatedDocuments Complete() => new()
    {
        Vin = Vin,
        GeneratedAt = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero),
        Sources =
        [
            SourceResult.Ok(SourceSystem.Sales, [], TimeSpan.FromMilliseconds(120)),
            SourceResult.Ok(SourceSystem.Service, [], TimeSpan.FromMilliseconds(80)),
        ],
        Documents =
        [
            new Document
            {
                SourceId = "INV-2024-0311",
                Source = SourceSystem.Sales,
                Vin = Vin,
                Title = "Sales Invoice",
                Type = DocumentType.Invoice,
                Date = new DateOnly(2024, 2, 11),
                ExpiryDate = new DateOnly(2027, 2, 11),
                ReferenceNumber = "BLR/24/0311",
                Metadata = new Dictionary<string, string> { ["amount"] = "845000.00" },
                Status = DocumentStatus.Valid,
                Flags = [DocumentMarker.PossibleDuplicate],
            },
        ],
    };

    [Fact]
    public async Task SetThenGet_RoundTripsEveryField()
    {
        var cache = Create(MemoryCache());
        var original = Complete();

        await cache.SetAsync(original, CancellationToken.None);
        var loaded = await cache.GetAsync(Vin, CancellationToken.None);

        loaded.ShouldNotBeNull();
        loaded.Vin.ShouldBe(Vin);
        loaded.GeneratedAt.ShouldBe(original.GeneratedAt);
        loaded.Sources.Select(s => (s.Source, s.Status, s.Latency)).ShouldBe(original.Sources.Select(s => (s.Source, s.Status, s.Latency)));
        var doc = loaded.Documents.ShouldHaveSingleItem();
        var expected = original.Documents[0];
        doc.Id.ShouldBe(expected.Id);
        doc.Date.ShouldBe(expected.Date);
        doc.ExpiryDate.ShouldBe(expected.ExpiryDate);
        doc.ReferenceNumber.ShouldBe(expected.ReferenceNumber);
        doc.Status.ShouldBe(expected.Status);
        doc.Flags.ShouldBe(expected.Flags);
        doc.Metadata["amount"].ShouldBe("845000.00");
    }

    [Fact]
    public async Task Get_UnknownVin_IsMiss()
    {
        (await Create(MemoryCache()).GetAsync(Vin, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Set_UsesConfiguredTtlAndVersionedKey()
    {
        var inner = Substitute.For<IDistributedCache>();

        await Create(inner, ttlSeconds: 45).SetAsync(Complete(), CancellationToken.None);

        await inner.Received(1).SetAsync(
            "docs:v1:MA3EWB81S00100006",
            Arg.Any<byte[]>(),
            Arg.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromSeconds(45)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Invariant", "4")]
    public async Task Set_PartialResult_IsNeverStored()
    {
        var inner = Substitute.For<IDistributedCache>();
        var partial = Complete() with
        {
            Sources =
            [
                SourceResult.Ok(SourceSystem.Sales, [], TimeSpan.Zero),
                SourceResult.Failed(SourceSystem.Service, SourceStatus.Timeout, "timeout", TimeSpan.FromSeconds(2)),
            ],
        };

        await Create(inner).SetAsync(partial, CancellationToken.None);

        await inner.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, default!, default);
    }

    [Fact]
    [Trait("Invariant", "10")]
    public async Task CacheOutage_GetIsMiss_SetIsNoOp_NoExceptions()
    {
        var inner = Substitute.For<IDistributedCache>();
        inner.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("redis down"));
        inner.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("redis down"));
        var cache = Create(inner);
        using var lookups = _metrics.Collect<long>("docviewer.cache.requests");

        (await cache.GetAsync(Vin, CancellationToken.None)).ShouldBeNull();
        await Should.NotThrowAsync(() => cache.SetAsync(Complete(), CancellationToken.None));
        lookups.GetMeasurementSnapshot().Select(m => m.Tags["result"]).ShouldBe(["error", "error"]);
    }

    [Fact]
    public async Task CorruptEntry_IsTreatedAsMiss()
    {
        var inner = MemoryCache();
        await inner.SetAsync(DistributedDocumentCache.KeyFor(Vin), "not json"u8.ToArray(), new DistributedCacheEntryOptions());

        (await Create(inner).GetAsync(Vin, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        var inner = Substitute.For<IDistributedCache>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        inner.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException(cts.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => Create(inner).GetAsync(Vin, cts.Token));
    }
}
