namespace DocViewer.Domain.Tests;

internal static class TestDocuments
{
    public static readonly Vin Vin = Vin.Parse("MA3EWB81S00100001");

    public static Document Create(
        string sourceId,
        SourceSystem source = SourceSystem.Sales,
        DocumentType type = DocumentType.Invoice,
        string date = "2025-03-14",
        string? expiry = null,
        string? reference = null) =>
        new()
        {
            SourceId = sourceId,
            Source = source,
            Vin = Vin,
            Title = $"{type} {sourceId}",
            Type = type,
            Date = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
            ExpiryDate = expiry is null ? null : DateOnly.Parse(expiry, System.Globalization.CultureInfo.InvariantCulture),
            ReferenceNumber = reference,
        };
}
