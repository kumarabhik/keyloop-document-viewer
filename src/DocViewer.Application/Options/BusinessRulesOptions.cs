using System.ComponentModel.DataAnnotations;

namespace DocViewer.Application.Options;

/// <summary>Business rules for document status (DESIGN.md §9.2).</summary>
public sealed class BusinessRulesOptions : IValidatableObject
{
    public const string SectionName = "BusinessRules";

    /// <summary>Documents expiring within this many days are <c>ExpiringSoon</c>.</summary>
    [Range(0, 365)]
    public int ExpiryWarningDays { get; set; } = 30;

    /// <summary>IANA timezone of the dealership; "today" is evaluated here (assumption A10).</summary>
    [Required]
    public string TimeZone { get; set; } = "Asia/Kolkata";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _))
        {
            yield return new ValidationResult($"Unknown timezone '{TimeZone}'.", [nameof(TimeZone)]);
        }
    }
}
