using System.ComponentModel.DataAnnotations;

namespace DocViewer.Application.Options;

/// <summary>Aggregated-response cache settings (D-006, D-015).</summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    [Range(1, 3600)]
    public int TtlSeconds { get; set; } = 60;

    public TimeSpan Ttl => TimeSpan.FromSeconds(TtlSeconds);
}
