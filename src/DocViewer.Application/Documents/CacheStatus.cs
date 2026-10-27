namespace DocViewer.Application.Documents;

public enum CacheStatus
{
    Hit,
    Miss,

    /// <summary>The caller asked for <c>refresh=true</c>; the cache was not read.</summary>
    Bypass,
}
