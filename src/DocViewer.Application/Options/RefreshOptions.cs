using System.ComponentModel.DataAnnotations;

namespace DocViewer.Application.Options;

/// <summary>
/// Background refresh of partial results (D-033): a VIN whose fan-out missed a source is searched again later, so a
/// complete result reaches the cache and the snapshots stay fresh without a user waiting for it.
/// </summary>
public sealed class RefreshOptions
{
    public const string SectionName = "Refresh";

    public bool Enabled { get; set; } = true;

    /// <summary>Wait before the first retry; each later attempt waits three times longer (5 s, 15 s, 45 s by default).</summary>
    [Range(0.05, 600)]
    public double FirstDelaySeconds { get; set; } = 5;

    /// <summary>Background fan-outs per VIN before giving up; the next user search starts again.</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Background fan-outs running at once, so a burst of partial results cannot flood the sources.</summary>
    [Range(1, 32)]
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>VINs waiting for a refresh; beyond this new ones are dropped (and counted).</summary>
    [Range(1, 100_000)]
    public int QueueCapacity { get; set; } = 1000;

    public TimeSpan DelayBefore(int attempt) => TimeSpan.FromSeconds(FirstDelaySeconds * Math.Pow(3, attempt - 1));
}
