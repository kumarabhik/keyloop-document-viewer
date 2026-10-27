namespace DocViewer.Domain;

/// <summary>Computed from the expiry date (DESIGN.md §9.2); never taken from a source.</summary>
public enum DocumentStatus
{
    NotApplicable,
    Valid,
    ExpiringSoon,
    Expired,
}
