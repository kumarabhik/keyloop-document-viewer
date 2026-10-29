namespace DocViewer.Domain.Tests;

/// <summary>D-012 sort order as one type; pagination cursors are positions in it (D-031).</summary>
public sealed class DocumentPositionTests
{
    private static DocumentPosition At(string date, SourceSystem source, string id) => new(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), source, id);

    [Theory]
    [InlineData("2025-06-01", SourceSystem.Sales, "A", "2025-01-01", SourceSystem.Sales, "A")]     // newer date first
    [InlineData("2025-01-01", SourceSystem.Sales, "Z", "2025-01-01", SourceSystem.Service, "A")]   // then Sales before Service
    [InlineData("2025-01-01", SourceSystem.Sales, "INV-10", "2025-01-01", SourceSystem.Sales, "INV-9")] // then ordinal id
    public void FirstComesBeforeSecond(string d1, SourceSystem s1, string id1, string d2, SourceSystem s2, string id2)
    {
        var first = At(d1, s1, id1);
        var second = At(d2, s2, id2);

        (first < second).ShouldBeTrue();
        (second > first).ShouldBeTrue();
        first.CompareTo(second).ShouldBeLessThan(0);
    }

    [Fact]
    public void EqualPositions_CompareEqual()
    {
        var a = At("2025-01-01", SourceSystem.Sales, "X");

        a.CompareTo(At("2025-01-01", SourceSystem.Sales, "X")).ShouldBe(0);
        (a <= At("2025-01-01", SourceSystem.Sales, "X")).ShouldBeTrue();
    }

    [Fact]
    [Trait("Invariant", "8")]
    public void MergerOutput_IsInPositionOrder()
    {
        Document Doc(string id, SourceSystem source, string date) => new()
        {
            SourceId = id,
            Source = source,
            Vin = Vin.Parse("MA3EWB81S00100001"),
            Title = id,
            Type = DocumentType.JobCard,
            Date = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
        };

        var merged = DocumentMerger.Merge(
            [Doc("B", SourceSystem.Service, "2025-01-01"), Doc("A", SourceSystem.Sales, "2025-01-01"), Doc("C", SourceSystem.Sales, "2026-01-01"), Doc("A2", SourceSystem.Sales, "2025-01-01")],
            new DateOnly(2026, 10, 30),
            30);

        merged.Select(DocumentPosition.Of).ShouldBeInOrder(SortDirection.Ascending);
        merged.Select(d => d.SourceId).ShouldBe(["C", "A", "A2", "B"]);
    }
}
