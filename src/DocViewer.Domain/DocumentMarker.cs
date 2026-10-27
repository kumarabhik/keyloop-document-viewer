namespace DocViewer.Domain;

public enum DocumentMarker
{
    /// <summary>Same document found in another source with identical dates (D-009).</summary>
    PossibleDuplicate,

    /// <summary>Same document found in another source with different dates (D-009).</summary>
    Conflict,

    /// <summary>Served from a last-known-good snapshot because the source was unavailable (D-003).</summary>
    Stale,
}
