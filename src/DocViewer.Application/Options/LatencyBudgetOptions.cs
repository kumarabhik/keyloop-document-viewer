using System.ComponentModel.DataAnnotations;

namespace DocViewer.Application.Options;

/// <summary>
/// Time budgets for the calls a search makes besides the sources (those are D-005). Without them a stopped Postgres
/// or Redis held every search for ~12 s and broke invariant 5 (E-018, E-019). Running out of budget means "no
/// snapshot" or "cache miss", never an error (invariant 10).
/// </summary>
public sealed class LatencyBudgetOptions
{
    public const string SectionName = "LatencyBudget";

    public static readonly TimeSpan DefaultSnapshotReadTimeout = TimeSpan.FromMilliseconds(300);

    public static readonly TimeSpan DefaultCacheTimeout = TimeSpan.FromMilliseconds(150);

    /// <summary>Reading the last-known-good snapshot of a failed source; runs after the source used its 2 s.</summary>
    [Range(10, 2000)]
    public int SnapshotReadTimeoutMs { get; set; } = (int)DefaultSnapshotReadTimeout.TotalMilliseconds;

    public TimeSpan SnapshotReadTimeout => TimeSpan.FromMilliseconds(SnapshotReadTimeoutMs);

    /// <summary>
    /// Each cache read and each cache write. Whole searches with a healthy Redis took 0.09–0.23 s on 2026-10-29, so
    /// this is generous; a write that runs out of time finishes on its own, off the request path.
    /// </summary>
    [Range(10, 1000)]
    public int CacheTimeoutMs { get; set; } = (int)DefaultCacheTimeout.TotalMilliseconds;

    public TimeSpan CacheTimeout => TimeSpan.FromMilliseconds(CacheTimeoutMs);
}
