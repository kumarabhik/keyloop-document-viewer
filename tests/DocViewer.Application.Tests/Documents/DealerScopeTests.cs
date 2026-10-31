using DocViewer.Application.Documents;
using DocViewer.Application.Sources;
using DocViewer.Domain;
using static DocViewer.Application.Tests.Documents.TestData;

namespace DocViewer.Application.Tests.Documents;

/// <summary>D-034: what a dealer may see of one aggregated result.</summary>
public sealed class DealerScopeTests
{
    private static readonly DateOnly Today = new(2026, 10, 31);

    private static Document Owned(string id, SourceSystem source, string? dealer, string? reference = null) =>
        Doc(id, source) with { DealerCode = dealer, ReferenceNumber = reference, Type = DocumentType.Invoice };

    private static AggregatedDocuments Aggregate(params Document[] documents)
    {
        var merged = DocumentMerger.Merge(documents, Today, 30);
        return new AggregatedDocuments
        {
            Vin = TestVin,
            Sources =
            [
                SourceResult.Ok(SourceSystem.Sales, [.. documents.Where(d => d.Source == SourceSystem.Sales)], TimeSpan.Zero),
                // As the aggregator builds it: a failure, then its snapshot stands in (stale).
                SourceResult.Failed(SourceSystem.Service, SourceStatus.Timeout, "timeout", TimeSpan.Zero) with
                {
                    Status = SourceStatus.Stale,
                    Documents = [.. documents.Where(d => d.Source == SourceSystem.Service)],
                },
            ],
            Documents = merged,
            GeneratedAt = DateTimeOffset.UnixEpoch,
        };
    }

    [Theory]
    [InlineData("NEXA-KOR", true)]
    [InlineData("nexa-kor", true)]
    [InlineData("NEXA-WFD", false)]
    [InlineData(null, false)] // fail closed
    public void Allows_MatchesDealerCodesCaseInsensitively_AndHidesDocumentsWithoutOne(string? dealer, bool expected)
    {
        new DealerScope([" NEXA-KOR "]).Allows(Owned("D1", SourceSystem.Sales, dealer)).ShouldBe(expected);
    }

    [Fact]
    public void EmptyScope_SeesNothing()
    {
        new DealerScope(["", "  "]).Apply(Aggregate(Owned("D1", SourceSystem.Sales, "NEXA-KOR")), Today, 30)
            .Documents.ShouldBeEmpty();
    }

    [Fact]
    public void Apply_KeepsOnlyTheDealersDocuments_InSourcesToo_AndLeavesSourceHealthAlone()
    {
        var all = Aggregate(
            Owned("INV-1", SourceSystem.Sales, "NEXA-KOR", reference: "R1"),
            Owned("SV-INV-R1", SourceSystem.Service, "NEXA-WFD", reference: "R1"),
            Owned("SV-JC-2", SourceSystem.Service, "NEXA-KOR"));
        all.Documents.Count(d => d.Flags.Contains(DocumentMarker.PossibleDuplicate)).ShouldBe(2);

        var scoped = new DealerScope(["NEXA-KOR"]).Apply(all, Today, 30);

        scoped.Documents.Select(d => d.Id).ShouldBe(["SALES:INV-1", "SERVICE:SV-JC-2"], ignoreOrder: true);
        scoped.Documents.ShouldAllBe(d => d.Flags.Count == 0 && !d.Metadata.ContainsKey(DocumentMerger.DuplicateOfKey));
        scoped.Sources.Single(s => s.Source == SourceSystem.Service).Documents.Select(d => d.SourceId).ShouldBe(["SV-JC-2"]);
        scoped.Sources.Select(s => s.Status).ShouldBe(all.Sources.Select(s => s.Status));
        scoped.Partial.ShouldBe(all.Partial);
    }

    [Fact]
    public void Apply_KeepsMarksBetweenDocumentsTheScopeSeesBoth()
    {
        var all = Aggregate(
            Owned("INV-1", SourceSystem.Sales, "NEXA-KOR", reference: "R1"),
            Owned("SV-INV-R1", SourceSystem.Service, "NEXA-WFD", reference: "R1"));

        var scoped = new DealerScope(["NEXA-KOR", "NEXA-WFD"]).Apply(all, Today, 30);

        scoped.Documents.ShouldAllBe(d => d.Flags.SequenceEqual(new[] { DocumentMarker.PossibleDuplicate }));
    }
}
