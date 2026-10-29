using System.ComponentModel.DataAnnotations;

namespace DocViewer.Api.Http;

/// <summary>
/// Searches allowed per client IP per minute (R5, Kumar 2026-10-30). Without a limit anyone who can reach the API can
/// try VIN after VIN. Load tests and the CI contract fuzzer raise it explicitly (README §7.1, ci.yml).
/// </summary>
internal sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    [Range(1, 1_000_000)]
    public int PermitsPerMinute { get; set; } = 60;
}
