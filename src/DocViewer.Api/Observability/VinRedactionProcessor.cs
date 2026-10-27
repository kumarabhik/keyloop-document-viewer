using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace DocViewer.Api.Observability;

/// <summary>
/// HTTP spans record request URLs (<c>url.path</c>, <c>url.full</c>, the display name of unmatched routes), and
/// our URLs contain the VIN. This processor masks VIN-shaped values in every span before export (D-011).
/// It runs on span end, after every instrumentation has added its tags.
/// </summary>
internal sealed partial class VinRedactionProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        List<KeyValuePair<string, string>>? changes = null;
        foreach (var tag in data.TagObjects)
        {
            if (tag.Value is string text && Redact(text) is var redacted && !ReferenceEquals(redacted, text))
            {
                (changes ??= []).Add(new(tag.Key, redacted));
            }
        }

        foreach (var (key, value) in changes ?? [])
        {
            data.SetTag(key, value);
        }

        data.DisplayName = Redact(data.DisplayName);
    }

    /// <summary>
    /// Masks each standalone 17-character alphanumeric token like <see cref="DocViewer.Domain.Vin.Masked"/> (WMI + last 4).
    /// Invalid VINs are masked too: a one-character typo still identifies a vehicle. Returns the same
    /// instance if nothing matched.
    /// </summary>
    internal static string Redact(string value) =>
        VinLike().IsMatch(value) ? VinLike().Replace(value, m => string.Concat(m.Value.AsSpan(0, 3), "**********", m.Value.AsSpan(13))) : value;

    // Standalone: not part of a longer alphanumeric run (so a 32-char trace id is never touched).
    [GeneratedRegex("(?<![A-Za-z0-9])[A-Za-z0-9]{17}(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex VinLike();
}
