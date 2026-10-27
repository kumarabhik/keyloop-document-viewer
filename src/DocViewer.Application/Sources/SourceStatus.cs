namespace DocViewer.Application.Sources;

public enum SourceStatus
{
    Ok,

    /// <summary>Source failed; documents come from the last-known-good snapshot (D-003).</summary>
    Stale,
    Timeout,
    Error,
    CircuitOpen,
}
