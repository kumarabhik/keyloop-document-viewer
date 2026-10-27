using System.ComponentModel.DataAnnotations;

namespace DocViewer.Infrastructure.Options;

/// <summary>
/// Per-source resilience budget. Defaults are the values fixed by D-005 and D-014;
/// change them only with a new decision entry in SYSTEMS.md.
/// </summary>
public sealed class ResilienceOptions : IValidatableObject
{
    public const string SectionName = "Resilience";

    /// <summary>Whole budget for one source call, retry included.</summary>
    [Range(0.1, 30)]
    public double TotalTimeoutSeconds { get; set; } = 2.0;

    [Range(0.1, 30)]
    public double AttemptTimeoutSeconds { get; set; } = 1.5;

    [Range(0.05, 10)]
    public double ConnectTimeoutSeconds { get; set; } = 0.5;

    [Range(0, 3)]
    public int MaxRetryAttempts { get; set; } = 1;

    [Range(0, 5000)]
    public int RetryBaseDelayMilliseconds { get; set; } = 100;

    [Range(0.01, 1.0)]
    public double BreakerFailureRatio { get; set; } = 0.5;

    [Range(2, 1000)]
    public int BreakerMinimumThroughput { get; set; } = 5;

    [Range(0.5, 600)]
    public double BreakerSamplingSeconds { get; set; } = 10;

    [Range(0.5, 600)]
    public double BreakerBreakSeconds { get; set; } = 30;

    public TimeSpan TotalTimeout => TimeSpan.FromSeconds(TotalTimeoutSeconds);
    public TimeSpan AttemptTimeout => TimeSpan.FromSeconds(AttemptTimeoutSeconds);
    public TimeSpan ConnectTimeout => TimeSpan.FromSeconds(ConnectTimeoutSeconds);
    public TimeSpan RetryBaseDelay => TimeSpan.FromMilliseconds(RetryBaseDelayMilliseconds);
    public TimeSpan BreakerSamplingDuration => TimeSpan.FromSeconds(BreakerSamplingSeconds);
    public TimeSpan BreakerBreakDuration => TimeSpan.FromSeconds(BreakerBreakSeconds);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AttemptTimeoutSeconds > TotalTimeoutSeconds)
        {
            yield return new ValidationResult(
                "AttemptTimeoutSeconds cannot exceed TotalTimeoutSeconds (D-005).",
                [nameof(AttemptTimeoutSeconds), nameof(TotalTimeoutSeconds)]);
        }

        if (ConnectTimeoutSeconds > AttemptTimeoutSeconds)
        {
            yield return new ValidationResult(
                "ConnectTimeoutSeconds cannot exceed AttemptTimeoutSeconds.",
                [nameof(ConnectTimeoutSeconds), nameof(AttemptTimeoutSeconds)]);
        }
    }
}
