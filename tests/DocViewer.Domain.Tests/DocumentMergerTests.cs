using static DocViewer.Domain.Tests.TestDocuments;

namespace DocViewer.Domain.Tests;

public class DocumentMergerTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    private static IReadOnlyList<Document> Merge(params Document[] documents) =>
        DocumentMerger.Merge(documents, Today, warningDays: 30);

    [Fact]
    public void Merge_Empty_ReturnsEmpty()
    {
        Merge().ShouldBeEmpty();
    }

    [Fact]
    public void Merge_KeepsEveryDocument()
    {
        var result = Merge(
            Create("A", SourceSystem.Sales),
            Create("B", SourceSystem.Service, DocumentType.JobCard),
            Create("C", SourceSystem.Service, DocumentType.Puc));

        result.Select(d => d.Id).ShouldBe(["SALES:A", "SERVICE:B", "SERVICE:C"], ignoreOrder: true);
    }

    [Fact]
    public void Merge_ComputesStatusForEachDocument()
    {
        var result = Merge(
            Create("expired", type: DocumentType.Insurance, expiry: "2026-10-01"),
            Create("soon", type: DocumentType.Insurance, expiry: "2026-11-05"),
            Create("valid", type: DocumentType.Insurance, expiry: "2027-10-01"),
            Create("none", type: DocumentType.Invoice));

        result.Single(d => d.SourceId == "expired").Status.ShouldBe(DocumentStatus.Expired);
        result.Single(d => d.SourceId == "soon").Status.ShouldBe(DocumentStatus.ExpiringSoon);
        result.Single(d => d.SourceId == "valid").Status.ShouldBe(DocumentStatus.Valid);
        result.Single(d => d.SourceId == "none").Status.ShouldBe(DocumentStatus.NotApplicable);
    }

    [Fact]
    [Trait("Invariant", "8")]
    public void Merge_SortsByDateDescending_ThenSource_ThenSourceId()
    {
        var result = Merge(
            Create("B", SourceSystem.Service, DocumentType.JobCard, "2025-01-01"),
            Create("Z", SourceSystem.Sales, DocumentType.Rc, "2025-01-01"),
            Create("A", SourceSystem.Service, DocumentType.JobCard, "2025-01-01"),
            Create("OLD", SourceSystem.Sales, DocumentType.Rc, "2024-01-01"),
            Create("NEW", SourceSystem.Service, DocumentType.Puc, "2026-01-01"));

        result.Select(d => d.Id).ShouldBe(["SERVICE:NEW", "SALES:Z", "SERVICE:A", "SERVICE:B", "SALES:OLD"]);
    }

    [Fact]
    [Trait("Invariant", "8")]
    public void Merge_OrderDoesNotDependOnInputOrder()
    {
        Document[] docs =
        [
            Create("1", SourceSystem.Sales, DocumentType.Rc, "2025-05-05"),
            Create("2", SourceSystem.Service, DocumentType.JobCard, "2025-05-05"),
            Create("3", SourceSystem.Sales, DocumentType.Insurance, "2025-05-05"),
        ];

        Merge(docs).Select(d => d.Id).ShouldBe(Merge([.. docs.Reverse()]).Select(d => d.Id));
    }

    [Fact]
    public void Merge_SameReferenceAndDatesAcrossSources_FlagsBothAsPossibleDuplicate()
    {
        var result = Merge(
            Create("INV-1", SourceSystem.Sales, reference: "BLR/24/0311"),
            Create("SV-INV-1", SourceSystem.Service, reference: "blr/24/0311 "));

        result.ShouldAllBe(d => d.Flags.Contains(DocumentMarker.PossibleDuplicate));
        result.Single(d => d.Source == SourceSystem.Sales).Metadata[DocumentMerger.DuplicateOfKey].ShouldBe("SERVICE:SV-INV-1");
        result.Single(d => d.Source == SourceSystem.Service).Metadata[DocumentMerger.DuplicateOfKey].ShouldBe("SALES:INV-1");
    }

    [Fact]
    public void Merge_SameReferenceDifferentExpiryAcrossSources_FlagsBothAsConflict()
    {
        var result = Merge(
            Create("W-1", SourceSystem.Sales, DocumentType.Warranty, expiry: "2027-02-11", reference: "WTY-77"),
            Create("W-9", SourceSystem.Service, DocumentType.Warranty, expiry: "2028-02-11", reference: "WTY-77"));

        result.ShouldAllBe(d => d.Flags.Contains(DocumentMarker.Conflict));
        result.ShouldAllBe(d => !d.Flags.Contains(DocumentMarker.PossibleDuplicate));
        result.ShouldAllBe(d => d.Metadata.ContainsKey(DocumentMerger.ConflictsWithKey));
    }

    [Fact]
    public void Merge_SameReferenceWithinOneSource_IsNotFlagged()
    {
        var result = Merge(
            Create("INV-1", SourceSystem.Sales, reference: "REF-1"),
            Create("INV-2", SourceSystem.Sales, reference: "REF-1"));

        result.ShouldAllBe(d => d.Flags.Count == 0);
    }

    [Fact]
    public void Merge_SameReferenceButDifferentType_IsNotFlagged()
    {
        var result = Merge(
            Create("A", SourceSystem.Sales, DocumentType.Invoice, reference: "REF-1"),
            Create("B", SourceSystem.Service, DocumentType.JobCard, reference: "REF-1"));

        result.ShouldAllBe(d => d.Flags.Count == 0);
    }

    [Fact]
    public void Merge_InvoicesWithoutReferenceOnSameDate_AreMatchedByDate()
    {
        var result = Merge(
            Create("A", SourceSystem.Sales, DocumentType.Invoice, "2024-02-11"),
            Create("B", SourceSystem.Service, DocumentType.Invoice, "2024-02-11"));

        result.ShouldAllBe(d => d.Flags.Contains(DocumentMarker.PossibleDuplicate));
    }

    [Fact]
    public void Merge_JobCardsWithoutReferenceOnSameDate_AreNotMatched()
    {
        var result = Merge(
            Create("A", SourceSystem.Sales, DocumentType.JobCard, "2024-02-11"),
            Create("B", SourceSystem.Service, DocumentType.JobCard, "2024-02-11"));

        result.ShouldAllBe(d => d.Flags.Count == 0);
    }

    [Fact]
    public void Merge_PreservesExistingMarkers()
    {
        var stale = Create("A", SourceSystem.Service, reference: "R") with { Flags = [DocumentMarker.Stale] };

        var result = Merge(stale, Create("B", SourceSystem.Sales, reference: "R"));

        result.Single(d => d.SourceId == "A").Flags.ShouldBe([DocumentMarker.Stale, DocumentMarker.PossibleDuplicate]);
    }

    [Fact]
    public void Merge_DoesNotMutateInputMetadata()
    {
        var original = Create("A", SourceSystem.Sales, reference: "R");

        Merge(original, Create("B", SourceSystem.Service, reference: "R"));

        original.Metadata.ShouldBeEmpty();
        original.Flags.ShouldBeEmpty();
    }

    [Fact]
    public void ClearCrossSourceMarks_RemovesTheMarksAndTheOtherDocumentsIds_ButNothingElse()
    {
        var merged = Merge(
            TestDocuments.Create("INV-1", SourceSystem.Sales, reference: "R1"),
            TestDocuments.Create("SV-INV-R1", SourceSystem.Service, reference: "R1"));
        var invoice = merged.Single(d => d.Source == SourceSystem.Sales) with
        {
            Metadata = new Dictionary<string, string>(merged.Single(d => d.Source == SourceSystem.Sales).Metadata) { ["dealer"] = "x" },
        };
        invoice.Flags.ShouldBe([DocumentMarker.PossibleDuplicate]);

        var cleared = DocumentMerger.ClearCrossSourceMarks(invoice);

        cleared.Flags.ShouldBeEmpty();
        cleared.Metadata.Keys.ShouldBe(["dealer"]); // duplicateOf (the other document's id) is gone
        cleared.Status.ShouldBe(invoice.Status);
    }

    [Fact]
    public void ClearCrossSourceMarks_LeavesAnUnmarkedDocumentAsItIs()
    {
        var document = TestDocuments.Create("INV-1");

        DocumentMerger.ClearCrossSourceMarks(document).ShouldBeSameAs(document);
    }
}
