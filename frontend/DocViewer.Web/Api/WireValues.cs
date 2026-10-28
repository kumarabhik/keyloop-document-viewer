namespace DocViewer.Web.Api;

/// <summary>Wire spellings the UI branches on (DESIGN.md §7.2, D-021).</summary>
internal static class WireValues
{
    public const string Sales = "SALES";
    public const string Service = "SERVICE";

    public const string Ok = "ok";
    public const string Stale = "stale";

    public const string Expired = "EXPIRED";
    public const string ExpiringSoon = "EXPIRING_SOON";
    public const string Valid = "VALID";
    public const string NotApplicable = "NOT_APPLICABLE";

    public const string PossibleDuplicate = "POSSIBLE_DUPLICATE";
    public const string Conflict = "CONFLICT";

    public static IReadOnlyList<string> SourceNames { get; } = [Sales, Service];

    public static IReadOnlyList<string> DocumentTypes { get; } =
        ["INVOICE", "RC", "INSURANCE", "PUC", "WARRANTY", "JOB_CARD", "SERVICE_HISTORY", "OTHER"];

    public static IReadOnlyList<string> DocumentStatuses { get; } = [Expired, ExpiringSoon, Valid, NotApplicable];

    /// <summary><c>SERVICE_HISTORY</c> → <c>Service history</c>; <c>RC</c> and <c>PUC</c> stay acronyms.</summary>
    public static string Label(string wireValue) => wireValue switch
    {
        "RC" or "PUC" => wireValue,
        _ => string.Concat(wireValue[..1], wireValue[1..].Replace('_', ' ').ToLowerInvariant()),
    };
}
