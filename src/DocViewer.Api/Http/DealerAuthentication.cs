using System.Security.Claims;
using DocViewer.Application.Documents;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace DocViewer.Api.Http;

/// <summary>
/// JWT bearer validation against any OIDC issuer, a policy that requires a dealer claim, and the dealer scope taken
/// from the token (D-034). Registers nothing but the options when <c>Auth:Enabled</c> is false.
/// </summary>
internal static class DealerAuthentication
{
    public const string PolicyName = "dealer";

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue($"{AuthOptions.SectionName}:{nameof(AuthOptions.Enabled)}", false);

    public static IServiceCollection AddDealerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .BindConfiguration(AuthOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (!IsEnabled(configuration))
        {
            return services;
        }

        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.Authority = auth.Authority;
            o.Audience = auth.Audience;
            o.RequireHttpsMetadata = auth.RequireHttpsMetadata;
            o.MapInboundClaims = false; // keep claim names as issued, so "dealer_id" and "roles" are found as configured
            o.TokenValidationParameters.RoleClaimType = auth.RoleClaim;
            o.TokenValidationParameters.NameClaimType = "sub";
        });
        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyName, p => p.RequireAuthenticatedUser().RequireClaim(auth.DealerClaim));
        return services;
    }

    /// <summary>The caller's dealer scope, or <c>null</c> when auth is off (everything is visible, as before).</summary>
    public static DealerScope? ScopeOf(ClaimsPrincipal user, AuthOptions auth) =>
        auth.Enabled ? new DealerScope(user.FindAll(auth.DealerClaim).Select(c => c.Value)) : null;

    /// <summary>With auth on only the admin role may bypass the cache; with auth off anyone may, as before.</summary>
    public static bool MayRefresh(ClaimsPrincipal user, AuthOptions auth) => !auth.Enabled || user.IsInRole(auth.AdminRole);
}
