using System.Text.Json;
using DocViewer.Application.Observability;
using DocViewer.Application.Sources;
using DocViewer.Domain;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace DocViewer.Infrastructure.Sources;

/// <summary>
/// Shared plumbing for source adapters: timing, and turning every failure into a
/// <see cref="SourceResult"/> so nothing escapes to the aggregator (AGENTS.md §4 rule 3).
/// Subclasses only fetch and map their own payload.
/// </summary>
public abstract class SourceAdapterBase(HttpClient httpClient, TimeProvider timeProvider, DocViewerMetrics metrics, ILogger logger)
    : ISourceAdapter
{
    protected HttpClient Http { get; } = httpClient;

    protected ILogger Logger { get; } = logger;

    public abstract SourceSystem Source { get; }

    /// <summary>Logs and counts a source record that could not be mapped; the rest of the payload is still used.</summary>
    protected void RecordDropped(string? recordId, string reason)
    {
        SourceLog.RecordDropped(Logger, Source, recordId ?? "(none)", reason);
        metrics.RecordDropped(Source, reason);
    }

    public async Task<SourceResult> GetDocumentsAsync(Vin vin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vin);
        var started = timeProvider.GetTimestamp();

        try
        {
            var fetched = await FetchAsync(vin, cancellationToken);
            var latency = timeProvider.GetElapsedTime(started);
            SourceLog.Fetched(Logger, Source, vin.Masked, fetched.Documents.Count, fetched.Dropped, latency.TotalMilliseconds);
            return SourceResult.Ok(Source, fetched.Documents, latency, fetched.Dropped);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // The caller gave up; that is not a source failure.
        }
        catch (Exception ex)
        {
            var (status, error) = Classify(ex);
            var latency = timeProvider.GetElapsedTime(started);
            SourceLog.Failed(Logger, Source, vin.Masked, status, error, latency.TotalMilliseconds, ex);
            return SourceResult.Failed(Source, status, error, latency);
        }
    }

    /// <summary>Fetches and maps documents. May throw; the base class classifies the failure.</summary>
    protected abstract Task<FetchedDocuments> FetchAsync(Vin vin, CancellationToken cancellationToken);

    private static (SourceStatus Status, string Error) Classify(Exception ex) => ex switch
    {
        TimeoutRejectedException => (SourceStatus.Timeout, "timeout"),
        OperationCanceledException => (SourceStatus.Timeout, "timeout"), // HttpClient.Timeout surfaces as TaskCanceledException
        BrokenCircuitException => (SourceStatus.CircuitOpen, "circuit_open"),
        SourceHttpException http => (SourceStatus.Error, $"http_{(int)http.StatusCode}"),
        HttpRequestException => (SourceStatus.Error, "connection_failed"),
        JsonException or NotSupportedException => (SourceStatus.Error, "invalid_payload"),
        _ => (SourceStatus.Error, "unexpected"),
    };

    protected sealed record FetchedDocuments(IReadOnlyList<Document> Documents, int Dropped);
}
