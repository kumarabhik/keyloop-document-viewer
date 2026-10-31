using DocViewer.Application.Refresh;

namespace DocViewer.Api;

/// <summary>Hosts the background refresher (D-033) for the lifetime of the API.</summary>
internal sealed class RefreshWorker(BackgroundRefresher refresher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await refresher.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown; queued jobs are in memory and dropped by design
        }
    }
}
