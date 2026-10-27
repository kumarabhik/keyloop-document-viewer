using System.Diagnostics;

namespace DocViewer.Application.Observability;

/// <summary>
/// Manual spans (DESIGN.md §12.3): <c>aggregate</c>, <c>cache.get</c>, <c>source.sales</c>, <c>source.service</c>,
/// <c>merge</c>, <c>snapshot.persist</c>. Tags carry enums and counts only, never a VIN (D-011).
/// </summary>
public static class DocViewerTracing
{
    public const string SourceName = "DocViewer";

    public static readonly ActivitySource Source = new(SourceName);
}
