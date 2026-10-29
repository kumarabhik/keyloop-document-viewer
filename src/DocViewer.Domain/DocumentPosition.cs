namespace DocViewer.Domain;

/// <summary>
/// Where a document sits in every list this system returns (D-012): newest <see cref="Date"/> first, then
/// <see cref="Source"/>, then <see cref="SourceId"/> (ordinal). The merger sorts by it and pagination cursors point
/// at it (D-031), so the two can never disagree about the order.
/// </summary>
public readonly record struct DocumentPosition(DateOnly Date, SourceSystem Source, string SourceId) : IComparable<DocumentPosition>
{
    public static DocumentPosition Of(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(document.Date, document.Source, document.SourceId);
    }

    public int CompareTo(DocumentPosition other)
    {
        var byDate = other.Date.CompareTo(Date); // descending: a later date comes first
        if (byDate != 0)
        {
            return byDate;
        }

        var bySource = Source.CompareTo(other.Source);
        return bySource != 0 ? bySource : string.CompareOrdinal(SourceId, other.SourceId);
    }

    public static bool operator <(DocumentPosition left, DocumentPosition right) => left.CompareTo(right) < 0;

    public static bool operator >(DocumentPosition left, DocumentPosition right) => left.CompareTo(right) > 0;

    public static bool operator <=(DocumentPosition left, DocumentPosition right) => left.CompareTo(right) <= 0;

    public static bool operator >=(DocumentPosition left, DocumentPosition right) => left.CompareTo(right) >= 0;
}
