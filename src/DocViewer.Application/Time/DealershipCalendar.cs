using DocViewer.Application.Options;
using Microsoft.Extensions.Options;

namespace DocViewer.Application.Time;

/// <summary>
/// Answers "what is today?" in the dealership's timezone. Expiry is a calendar-date rule,
/// so evaluating it in UTC would flip statuses at 05:30 IST instead of midnight (A10).
/// </summary>
public sealed class DealershipCalendar(TimeProvider timeProvider, IOptions<BusinessRulesOptions> options)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), _zone).DateTime);
}
