namespace DocViewer.Domain;

/// <summary>Expiry status rules from DESIGN.md §9.2.</summary>
public static class DocumentStatusCalculator
{
    public static DocumentStatus Calculate(DateOnly? expiryDate, DateOnly today, int warningDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(warningDays);

        if (expiryDate is not { } expiry)
        {
            return DocumentStatus.NotApplicable;
        }

        if (expiry < today)
        {
            return DocumentStatus.Expired;
        }

        return expiry <= today.AddDays(warningDays) ? DocumentStatus.ExpiringSoon : DocumentStatus.Valid;
    }
}
