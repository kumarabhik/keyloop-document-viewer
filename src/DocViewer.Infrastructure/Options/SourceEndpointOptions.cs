using System.ComponentModel.DataAnnotations;

namespace DocViewer.Infrastructure.Options;

/// <summary>
/// Connection settings for one source system. Registered as named options, one per source,
/// bound from <c>Sources:{Name}</c> (e.g. <c>Sources:Sales</c>).
/// </summary>
public sealed class SourceEndpointOptions
{
    public const string SectionName = "Sources";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Per-source connection cap; keeps one slow source from starving the other (bulkhead).</summary>
    [Range(1, 1000)]
    public int MaxConnectionsPerServer { get; set; } = 50;
}
