using System.ComponentModel.DataAnnotations;

namespace DocViewer.Api.Http;

/// <summary>
/// OIDC bearer-token auth with dealer scoping (D-034). Off by default, so the demo, the UI, k6 and contract fuzzing run
/// as before; when on, every <c>/api/v1</c> call needs an access token from <see cref="Authority"/> for
/// <see cref="Audience"/> that carries at least one <see cref="DealerClaim"/>.
/// </summary>
internal sealed class AuthOptions : IValidatableObject
{
    public const string SectionName = "Auth";

    public bool Enabled { get; set; }

    /// <summary>The OIDC issuer; signing keys come from its discovery document.</summary>
    public string? Authority { get; set; }

    public string? Audience { get; set; }

    /// <summary>Only for a local issuer over plain HTTP; never turn off in production.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Claim holding a dealer code the caller may see; repeat it (or use a JSON array) for several dealers.</summary>
    [Required]
    public string DealerClaim { get; set; } = "dealer_id";

    [Required]
    public string RoleClaim { get; set; } = "roles";

    /// <summary>Role allowed to use <c>refresh=true</c>, which bypasses the cache and calls every source system.</summary>
    [Required]
    public string AdminRole { get; set; } = "admin";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled)
        {
            yield break;
        }

        if (!Uri.TryCreate(Authority, UriKind.Absolute, out _))
        {
            yield return new ValidationResult("Auth:Authority must be an absolute URL when Auth:Enabled is true.", [nameof(Authority)]);
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            yield return new ValidationResult("Auth:Audience is required when Auth:Enabled is true.", [nameof(Audience)]);
        }
    }
}
