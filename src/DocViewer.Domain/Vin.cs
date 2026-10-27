using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace DocViewer.Domain;

/// <summary>
/// A validated, normalised Vehicle Identification Number (ISO 3779 format).
/// The check digit is deliberately not enforced (D-008).
/// </summary>
[JsonConverter(typeof(VinJsonConverter))]
public sealed record Vin
{
    public const int Length = 17;
    private const int MaskPrefix = 3;
    private const int MaskSuffix = 4;

    private Vin(string value) => Value = value;

    public string Value { get; }

    /// <summary>WMI and last four characters only; the only form allowed in logs (D-011).</summary>
    public string Masked =>
        string.Concat(Value.AsSpan(0, MaskPrefix), new string('*', Length - MaskPrefix - MaskSuffix), Value.AsSpan(Length - MaskSuffix));

    public static bool TryParse(string? input, [NotNullWhen(true)] out Vin? vin, [NotNullWhen(false)] out string? error)
    {
        vin = null;
        var candidate = input?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(candidate))
        {
            error = "VIN is required.";
            return false;
        }

        if (candidate.Length != Length)
        {
            error = $"VIN must be {Length} characters (A-Z, 0-9, excluding I, O, Q). Got {candidate.Length} characters.";
            return false;
        }

        for (var i = 0; i < candidate.Length; i++)
        {
            var c = candidate[i];
            if (c is 'I' or 'O' or 'Q')
            {
                error = $"VIN cannot contain '{c}' (position {i + 1}); I, O and Q are not used in VINs.";
                return false;
            }

            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c))
            {
                error = $"VIN contains invalid character '{c}' at position {i + 1}; only A-Z and 0-9 are allowed.";
                return false;
            }
        }

        vin = new Vin(candidate);
        error = null;
        return true;
    }

    /// <summary>Parses trusted input (seed data, tests). Throws on invalid input.</summary>
    public static Vin Parse(string input) =>
        TryParse(input, out var vin, out var error) ? vin : throw new FormatException(error);

    public override string ToString() => Value;
}
