namespace DocViewer.Web.Api;

public sealed record SourceStatusResponse(string Name, string Status, long LatencyMs, int Count, DateTimeOffset? AsOf, string? Error)
{
    public bool IsOk => Status == WireValues.Ok;

    public bool IsStale => Status == WireValues.Stale;
}
