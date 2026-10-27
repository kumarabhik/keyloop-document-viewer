namespace DocViewer.Domain;

/// <summary>Canonical document types (DESIGN.md §8.2).</summary>
public enum DocumentType
{
    Invoice,
    Rc,
    Insurance,
    Puc,
    Warranty,
    JobCard,
    ServiceHistory,
    Other,
}
