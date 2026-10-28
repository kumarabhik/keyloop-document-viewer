using System.Text.Json;
using DocViewer.Domain;
using Microsoft.JSInterop;

namespace DocViewer.Web.Services;

/// <summary>
/// The last few searched VINs, kept in the browser's localStorage (the advisor's own browser; never sent anywhere).
/// Storage can be missing or blocked (private windows, previews), so every failure means "no history", never an error.
/// </summary>
internal sealed class RecentSearches(IJSRuntime js)
{
    public const string StorageKey = "docviewer.recentVins";
    public const int Capacity = 5;

    public async Task<IReadOnlyList<string>> GetAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            return Normalise(json is null ? [] : JsonSerializer.Deserialize(json, RecentSearchesJsonContext.Default.ListString) ?? []);
        }
        catch (Exception ex) when (ex is JSException or JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>Moves <paramref name="vin"/> to the front and returns the new list.</summary>
    public async Task<IReadOnlyList<string>> AddAsync(Vin vin)
    {
        ArgumentNullException.ThrowIfNull(vin);
        var updated = Normalise([vin.Value, .. await GetAsync()]);
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(updated, RecentSearchesJsonContext.Default.ListString));
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            // Storage full or blocked: the list still works for this page view.
        }

        return updated;
    }

    // Only valid VINs survive, so a hand-edited or old entry can never trigger a bad search.
    private static List<string> Normalise(IEnumerable<string> vins) =>
        vins.Select(v => Vin.TryParse(v, out var vin, out _) ? vin.Value : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(Capacity)
            .ToList();
}
