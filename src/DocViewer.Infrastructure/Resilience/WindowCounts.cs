namespace DocViewer.Infrastructure.Resilience;

/// <summary>Outcomes inside the current sampling window.</summary>
internal readonly record struct WindowCounts(long Successes, long Failures)
{
    public long Total => Successes + Failures;

    public double FailureRatio => Total == 0 ? 0 : (double)Failures / Total;
}
