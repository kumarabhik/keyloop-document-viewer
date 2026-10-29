using DocViewer.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace DocViewer.Api.Http;

/// <summary>
/// In Production, refuses the salts that are published in this repository (R6, D-029): with a known salt the audit
/// table's VIN hashes can be reversed by hashing a VIN range. Other environments (Demo, Development, Testing) accept
/// them, so <c>docker compose up</c> and <c>dotnet run</c> keep working without a .env file.
/// </summary>
internal sealed class ProductionSaltValidator(IHostEnvironment environment) : IValidateOptions<PersistenceOptions>
{
    private static readonly string[] PublishedSalts =
    [
        "local-demo-salt-change-me",       // docker-compose.yml default
        "change-me-to-a-long-random-value", // .env.example
        "local-development-salt",          // appsettings.Development.json
    ];

    public ValidateOptionsResult Validate(string? name, PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!environment.IsProduction())
        {
            return ValidateOptionsResult.Success;
        }

        var salt = options.VinHashSalt ?? "";
        return PublishedSalts.Contains(salt, StringComparer.Ordinal) || salt.Contains("change-me", StringComparison.OrdinalIgnoreCase)
            ? ValidateOptionsResult.Fail(
                "Persistence:VinHashSalt is a public demo value. In Production set VIN_HASH_SALT (Persistence__VinHashSalt) " +
                "to a long random secret; see .env.example.")
            : ValidateOptionsResult.Success;
    }
}
