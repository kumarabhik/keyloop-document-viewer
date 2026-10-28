using System.Globalization;
using DocViewer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DocViewer.Api.Http;

/// <summary>
/// Readiness: can we reach the snapshot/audit database (DESIGN.md §7.1)? The cache is optional and not checked.
/// An outage is <c>Degraded</c> (HTTP 200), not <c>Unhealthy</c>: searches still work without the database
/// (invariant 10), so a database blip must not pull every instance out of the load balancer (D-024).
/// </summary>
internal sealed class DatabaseHealthCheck(IDbContextFactory<DocViewerDbContext> contextFactory) : IHealthCheck
{
    public const string Name = "database";

    /// <summary>
    /// A stopped Postgres container took ~4 s to fail, longer than the 3 s container probe (D-023), which then
    /// reported a working API as unhealthy (E-012). Not answering within this time counts as <c>Degraded</c>.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        DocViewerDbContext db;
        try
        {
            db = await contextFactory.CreateDbContextAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded("Database check failed.", ex);
        }

        return await ProbeAsync(db.Database.CanConnectAsync(cancellationToken), Timeout, db.Dispose, cancellationToken);
    }

    /// <summary>
    /// Waits for <paramref name="connect"/> at most <paramref name="timeout"/>. The slow part of a failed connect does
    /// not always honour cancellation (E-012), so we stop waiting ourselves; <paramref name="release"/> runs once the
    /// connect has really finished, never while it is still using the connection.
    /// </summary>
    internal static async Task<HealthCheckResult> ProbeAsync(Task<bool> connect, TimeSpan timeout, Action release, CancellationToken cancellationToken)
    {
        _ = connect.ContinueWith(
            t =>
            {
                _ = t.Exception; // observed here; the outcome is reported below
                release();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            return await connect.WaitAsync(timeout, cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded("Database is not reachable.");
        }
        catch (TimeoutException)
        {
            return HealthCheckResult.Degraded(string.Create(CultureInfo.InvariantCulture, $"Database did not answer within {timeout.TotalSeconds:0.#} s."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded("Database check failed.", ex);
        }
    }
}
