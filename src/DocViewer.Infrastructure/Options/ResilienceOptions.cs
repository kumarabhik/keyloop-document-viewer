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

    /// <summary>
    /// Calls needed in the sampling window before the breaker may open. Was 5: at under 5 calls per 10 s a dead source
    /// never tripped it and every search paid the timeout (R4, measured 2026-10-29). 3 per D-027 (Kumar).
    /// </summary>
    [Range(2, 1000)]
    public int BreakerMinimumThroughput { get; set; } = 3;

    [Range(0.5, 600)]
    public double BreakerSamplingSeconds { get; set; } = 10;

    [Range(0.5, 600)]
    public double BreakerBreakSeconds { get; set; } = 30;

    /// <summary>
    /// Budget for each read or write of the shared breaker state in Redis (D-032). Up to three such calls happen per source
    /// call, all inside <see cref="TotalTimeoutSeconds"/>.
    /// </summary>
    [Range(5, 1000)]
    public int SharedStateTimeoutMilliseconds { get; set; } = 50;

    /// <summary>After a failed shared-state call, use local breaker state for this long before trying Redis again.</summary>
    [Range(1, 600)]
    public double SharedStateRetryAfterSeconds { get; set; } = 10;

    public TimeSpan SharedStateTimeout => TimeSpan.FromMilliseconds(SharedStateTimeoutMilliseconds);
    public TimeSpan SharedStateRetryAfter => TimeSpan.FromSeconds(SharedStateRetryAfterSeconds);

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
