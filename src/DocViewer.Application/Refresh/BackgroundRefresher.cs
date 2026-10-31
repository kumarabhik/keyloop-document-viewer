using DocViewer.Application.Documents;
using DocViewer.Application.Observability;
using DocViewer.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocViewer.Application.Refresh;

/// <summary>
/// Works through <see cref="RefreshScheduler"/> (D-033): each due job runs one fan-out for its VIN. A complete result is
/// cached and its snapshots saved by the normal fan-out path, so the next search is a complete cache hit; a result
/// that is still partial is retried with a longer delay, up to <see cref="RefreshOptions.MaxAttempts"/>.
/// </summary>
public sealed partial class BackgroundRefresher(
    RefreshScheduler queue,
    AggregationService aggregation,
    IOptions<RefreshOptions> options,
    DocViewerMetrics metrics,
    ILogger<BackgroundRefresher> logger)
{
    /// <summary>Runs until <paramref name="stoppingToken"/> is cancelled; then throws <see cref="OperationCanceledException"/>.</summary>
    public Task RunAsync(CancellationToken stoppingToken) =>
        Parallel.ForEachAsync(
            queue.ReadAllAsync(stoppingToken),
            new ParallelOptions { MaxDegreeOfParallelism = options.Value.MaxConcurrency, CancellationToken = stoppingToken },
            async (job, ct) => await ProcessAsync(job, ct));

    public async Task ProcessAsync(RefreshJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        bool complete;
        try
        {
            complete = await aggregation.RefreshAsync(job.Vin, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            queue.Complete(job.Vin);
            throw;
        }
        catch (Exception ex)
        {
            // The aggregator does not throw for source or infrastructure failures; this keeps one bug from stopping the worker.
            LogRefreshThrew(logger, job.Vin.Masked, job.Attempt, ex);
            complete = false;
        }

        if (complete)
        {
            queue.Complete(job.Vin);
            metrics.RefreshJob("completed");
            LogRefreshCompleted(logger, job.Vin.Masked, job.Attempt);
        }
        else if (job.Attempt >= options.Value.MaxAttempts)
        {
            queue.Complete(job.Vin);
            metrics.RefreshJob("gave_up");
            LogRefreshGaveUp(logger, job.Vin.Masked, job.Attempt);
        }
        else
        {
            queue.Retry(job);
            metrics.RefreshJob("retried");
        }
    }

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "refresh.completed vin={VinMasked} attempt={Attempt}")]
    private static partial void LogRefreshCompleted(ILogger logger, string vinMasked, int attempt);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Information, Message = "refresh.gave_up vin={VinMasked} attempts={Attempt}")]
    private static partial void LogRefreshGaveUp(ILogger logger, string vinMasked, int attempt);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Error, Message = "refresh.threw vin={VinMasked} attempt={Attempt}")]
    private static partial void LogRefreshThrew(ILogger logger, string vinMasked, int attempt, Exception exception);
}
