using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using DocViewer.Application.Observability;
using DocViewer.Domain;
using Microsoft.Extensions.Logging;

namespace DocViewer.Infrastructure.Sources.Service;

/// <summary>Adapter for the Service System: snake_case JSON, dd/MM/yyyy dates, empty list = no documents.</summary>
public sealed class ServiceAdapter(HttpClient httpClient, TimeProvider timeProvider, DocViewerMetrics metrics, ILogger<ServiceAdapter> logger)
    : SourceAdapterBase(httpClient, timeProvider, metrics, logger)
{
    private const string DateFormat = "dd/MM/yyyy";

    public override SourceSystem Source => SourceSystem.Service;

    protected override async Task<FetchedDocuments> FetchAsync(Vin vin, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(
            new Uri($"service/api/records?vin={Uri.EscapeDataString(vin.Value)}", UriKind.Relative),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SourceHttpException(response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync(ServiceJsonContext.Default.ServiceRecordsResponse, cancellationToken)
            ?? throw new JsonException("Empty Service payload.");

        var documents = new List<Document>();
        var dropped = 0;
        foreach (var record in payload.Records ?? [])
        {
            if (TryMap(record, vin, out var document, out var reason))
            {
                documents.Add(document);
            }
            else
            {
                dropped++;
                RecordDropped(record.RecordNo, reason);
            }
        }

        return new FetchedDocuments(documents, dropped);
    }

    private bool TryMap(ServiceRecord record, Vin vin, out Document document, out string reason)
    {
        document = null!;

        if (string.IsNullOrWhiteSpace(record.RecordNo))
        {
            reason = "missing_id";
            return false;
        }

        if (!TryParseDate(record.Created, out var created))
        {
            reason = "invalid_created";
            return false;
        }

        DateOnly? expiry = null;
        if (record.Expiry is not null)
        {
            if (!TryParseDate(record.Expiry, out var parsedExpiry))
            {
                reason = "invalid_expiry";
                return false;
            }

            expiry = parsedExpiry;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["category"] = record.Category ?? string.Empty,
        };
        if (!string.IsNullOrWhiteSpace(record.Workshop))
        {
            metadata["workshop"] = record.Workshop;
        }

        if (record.OdometerKm is { } km)
        {
            metadata["odometerKm"] = km.ToString(CultureInfo.InvariantCulture);
        }

        document = new Document
        {
            SourceId = record.RecordNo,
            Source = Source,
            Vin = vin,
            Title = string.IsNullOrWhiteSpace(record.Description) ? record.Category ?? "Record" : record.Description,
            Type = MapType(record.Category, record.RecordNo),
            Date = created,
            ExpiryDate = expiry,
            ReferenceNumber = ExtractReference(record.RecordNo, record.Category),
            DealerCode = string.IsNullOrWhiteSpace(record.DealerCode) ? null : record.DealerCode.Trim(), // the workshop's dealer (D-034)
            Metadata = metadata,
        };
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Service record numbers embed the business reference after a "SV-{category}-" prefix,
    /// e.g. "SV-INV-BLR/24/0311" → "BLR/24/0311". That reference is what Sales calls refNo.
    /// </summary>
    internal static string? ExtractReference(string recordNo, string? category)
    {
        var prefix = $"SV-{category}-";
        return category is not null && recordNo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && recordNo.Length > prefix.Length
            ? recordNo[prefix.Length..]
            : null;
    }

    private DocumentType MapType(string? category, string recordId)
    {
        switch (category)
        {
            case "INV": return DocumentType.Invoice;
            case "PUC": return DocumentType.Puc;
            case "WTY_CLAIM": return DocumentType.Warranty;
            case "JC": return DocumentType.JobCard;
            case "SVC": return DocumentType.ServiceHistory;
            default:
                SourceLog.UnknownType(Logger, Source, recordId, category ?? "(none)");
                return DocumentType.Other;
        }
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
