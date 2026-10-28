using System.Globalization;

namespace DocViewer.Web.Components;

/// <summary>Display text shared by the components. Dates are shown unambiguously (<c>12 Jun 2023</c>), never as 06/12.</summary>
internal static class Format
{
    public static string Date(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateOnly? date) => date is { } d ? Date(d) : "—";

    /// <summary>A timestamp in the viewer's own time zone.</summary>
    public static string Timestamp(DateTimeOffset value, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return TimeZoneInfo.ConvertTime(value, time.LocalTimeZone).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
    }

    public static DateOnly Today(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return DateOnly.FromDateTime(time.GetLocalNow().DateTime);
    }

    public static string Plural(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? "" : "s")}");

    /// <summary><c>expires in 10 days</c>, <c>expires tomorrow</c>, <c>expired 21 days ago</c>.</summary>
    public static string Expiry(DateOnly expiry, DateOnly today)
    {
        var days = expiry.DayNumber - today.DayNumber;
        return days switch
        {
            0 => "expires today",
            1 => "expires tomorrow",
            -1 => "expired yesterday",
            > 1 => string.Create(CultureInfo.InvariantCulture, $"expires in {days} days"),
            _ => string.Create(CultureInfo.InvariantCulture, $"expired {-days} days ago"),
        };
    }

    /// <summary>Plain-language reason a source is missing from the results.</summary>
    public static string SourceProblem(string status, string? error) => (status, error) switch
    {
        ("timeout", _) or (_, "timeout") => "did not answer in time",
        ("circuit_open", _) => "is paused after repeated failures",
        (_, { } e) when e.StartsWith("http_", StringComparison.Ordinal) => $"returned an error ({e.Replace("http_", "HTTP ", StringComparison.Ordinal)})",
        _ => "returned an error",
    };

    public static string SourceName(string source) => source == Api.WireValues.Sales ? "Sales system" : "Service system";
}
