using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocViewer.Application.Observability;
using DocViewer.Domain;
using Microsoft.Extensions.Logging;

namespace DocViewer.Infrastructure.Sources.Sales;

/// <summary>Adapter for the Sales System: camelCase JSON, ISO dates, 404 = no documents.</summary>
public sealed class SalesAdapter(HttpClient httpClient, TimeProvider timeProvider, DocViewerMetrics metrics, ILogger<SalesAdapter> logger)
    : SourceAdapterBase(httpClient, timeProvider, metrics, logger)
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string ReferenceKey = "refNo";

    public override SourceSystem Source => SourceSystem.Sales;

    protected override async Task<FetchedDocuments> FetchAsync(Vin vin, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(
            new Uri($"sales/v1/vehicles/{Uri.EscapeDataString(vin.Value)}/documents", UriKind.Relative),
            cancellationToken);

        // The Sales System answers 404 for vehicles it has never sold; that is "no documents", not a failure.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new FetchedDocuments([], 0);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SourceHttpException(response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync(SalesJsonContext.Default.SalesDocumentsResponse, cancellationToken)
            ?? throw new JsonException("Empty Sales payload.");

        var documents = new List<Document>();
        var dropped = 0;
        foreach (var record in payload.Documents ?? [])
        {
            if (TryMap(record, vin, payload.Vehicle?.DealerCode, out var document, out var reason))
            {
                documents.Add(document);
            }
            else
            {
                dropped++;
                RecordDropped(record.DocId, reason);
            }
        }

        return new FetchedDocuments(documents, dropped);
    }

    /// <param name="dealerCode">The selling dealer, from the vehicle: it issued every Sales paper of the car (D-034).</param>
    private bool TryMap(SalesDocument record, Vin vin, string? dealerCode, out Document document, out string reason)
    {
        document = null!;

        if (string.IsNullOrWhiteSpace(record.DocId))
        {
            reason = "missing_id";
            return false;
        }

        if (!TryParseDate(record.IssuedOn, out var issued))
        {
            reason = "invalid_issued_on";
            return false;
        }

        DateOnly? expiry = null;
        if (record.ValidUntil is not null)
        {
            if (!TryParseDate(record.ValidUntil, out var parsedExpiry))
            {
                reason = "invalid_valid_until";
                return false;
            }

            expiry = parsedExpiry;
        }

        var metadata = (record.Meta ?? []).ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? string.Empty : kv.Value.GetRawText(),
            StringComparer.Ordinal);
        metadata.Remove(ReferenceKey, out var reference);

        document = new Document
        {
            SourceId = record.DocId,
            Source = Source,
            Vin = vin,
            Title = string.IsNullOrWhiteSpace(record.Title) ? record.DocType ?? "Document" : record.Title,
            Type = MapType(record.DocType, record.DocId),
            Date = issued,
            ExpiryDate = expiry,
            ReferenceNumber = reference,
            DealerCode = string.IsNullOrWhiteSpace(dealerCode) ? null : dealerCode.Trim(),
            Metadata = metadata,
        };
        reason = string.Empty;
        return true;
    }

    private DocumentType MapType(string? rawType, string recordId)
    {
        switch (rawType)
        {
            case "SALE_INVOICE": return DocumentType.Invoice;
            case "REG_CERT": return DocumentType.Rc;
            case "INSURANCE_POLICY": return DocumentType.Insurance;
            case "WARRANTY_CARD": return DocumentType.Warranty;
            default:
                SourceLog.UnknownType(Logger, Source, recordId, rawType ?? "(none)");
                return DocumentType.Other;
        }
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
