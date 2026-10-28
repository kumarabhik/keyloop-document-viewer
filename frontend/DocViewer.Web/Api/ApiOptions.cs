using System.ComponentModel.DataAnnotations;

namespace DocViewer.Web.Api;

/// <summary>Bound from the <c>Api</c> section of <c>wwwroot/appsettings.json</c>.</summary>
internal sealed class ApiOptions
{
    public const string SectionName = "Api";

    [Required]
    public Uri BaseUrl { get; set; } = new("http://localhost:8080");

    /// <summary>Serve canned responses from <see cref="MockApiHandler"/> instead of calling the API (D4.5).</summary>
    public bool UseMocks { get; set; }

    /// <summary>Delay of each mock-mode response, so the loading state is visible. Tests set 0.</summary>
    [Range(0, 10_000)]
    public int MockLatencyMs { get; set; } = 400;

    /// <summary>Fault switches of the mock source systems, used by the health panel's demo toggles (D5.2).</summary>
    public Uri SalesMockUrl { get; set; } = new("http://localhost:8081");

    public Uri ServiceMockUrl { get; set; } = new("http://localhost:8082");
}
