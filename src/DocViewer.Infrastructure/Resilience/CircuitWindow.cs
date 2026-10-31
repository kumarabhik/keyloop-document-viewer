namespace DocViewer.Infrastructure.Resilience;

/// <summary>
/// The sampling window cut into ten buckets, like Polly's own breaker. Buckets are numbered from the Unix epoch, so every
/// replica puts an outcome at the same instant into the same bucket.
/// </summary>
internal readonly record struct CircuitWindow(TimeSpan Sampling)
{
    private const int Buckets = 10;

    public long BucketMilliseconds => Math.Max(1, (long)Sampling.TotalMilliseconds / Buckets);

    public long BucketOf(DateTimeOffset at) => at.ToUnixTimeMilliseconds() / BucketMilliseconds;

    /// <summary>The oldest bucket that still counts at <paramref name="now"/>.</summary>
    public long OldestBucket(DateTimeOffset now) => BucketOf(now) - Buckets + 1;

    /// <summary>How long a store must keep window data: one whole window after the last write.</summary>
    public TimeSpan Retention => Sampling + TimeSpan.FromMilliseconds(BucketMilliseconds);
}
