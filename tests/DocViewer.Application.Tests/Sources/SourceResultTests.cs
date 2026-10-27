using DocViewer.Application.Documents;
using DocViewer.Application.Sources;
using DocViewer.Domain;

namespace DocViewer.Application.Tests.Sources;

public class SourceResultTests
{
    [Theory]
    [InlineData(SourceStatus.Ok)]
    [InlineData(SourceStatus.Stale)]
    public void Failed_RejectsNonFailureStatus(SourceStatus status)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => SourceResult.Failed(SourceSystem.Sales, status, "x", TimeSpan.Zero));
    }

    [Fact]
    public void Failed_HasNoDocumentsAndIsNotOk()
    {
        var result = SourceResult.Failed(SourceSystem.Service, SourceStatus.Timeout, "timeout", TimeSpan.FromSeconds(2));

        result.IsOk.ShouldBeFalse();
        result.Documents.ShouldBeEmpty();
        result.Error.ShouldBe("timeout");
    }

    [Fact]
    [Trait("Invariant", "3")]
    public void Aggregated_IsPartial_WhenAnySourceIsNotOk()
    {
        var vin = Vin.Parse("MA3EWB81S00100001");
        AggregatedDocuments With(params SourceResult[] sources) =>
            new() { Vin = vin, Sources = sources, Documents = [], GeneratedAt = DateTimeOffset.UnixEpoch };

        var ok = SourceResult.Ok(SourceSystem.Sales, [], TimeSpan.Zero);
        var okToo = SourceResult.Ok(SourceSystem.Service, [], TimeSpan.Zero);
        var stale = okToo with { Status = SourceStatus.Stale };
        var failed = SourceResult.Failed(SourceSystem.Service, SourceStatus.Error, "http_500", TimeSpan.Zero);

        With(ok, okToo).Partial.ShouldBeFalse();
        With(ok, stale).Partial.ShouldBeTrue();
        With(ok, failed).Partial.ShouldBeTrue();
    }
}
