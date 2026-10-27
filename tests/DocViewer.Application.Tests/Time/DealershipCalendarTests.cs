using DocViewer.Application.Options;
using DocViewer.Application.Time;
using Microsoft.Extensions.Time.Testing;

namespace DocViewer.Application.Tests.Time;

public class DealershipCalendarTests
{
    private static DealershipCalendar CreateCalendar(FakeTimeProvider time, string timeZone = "Asia/Kolkata") =>
        new(time, Microsoft.Extensions.Options.Options.Create(new BusinessRulesOptions { TimeZone = timeZone }));

    [Fact]
    public void Today_BeforeIstMidnight_IsSameUtcDate()
    {
        // 18:29 UTC = 23:59 IST on 27 Oct
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 18, 29, 0, TimeSpan.Zero));

        CreateCalendar(time).Today.ShouldBe(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public void Today_AfterIstMidnight_IsNextDateEvenThoughUtcIsStillPreviousDay()
    {
        // 18:30 UTC = 00:00 IST on 28 Oct (assumption A10)
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 18, 30, 0, TimeSpan.Zero));

        CreateCalendar(time).Today.ShouldBe(new DateOnly(2026, 10, 28));
    }

    [Fact]
    public void Today_FollowsTimeProviderWhenTimeAdvances()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        var calendar = CreateCalendar(time);

        time.Advance(TimeSpan.FromDays(3));

        calendar.Today.ShouldBe(new DateOnly(2026, 10, 30));
    }

    [Fact]
    public void Today_UsesConfiguredTimeZone()
    {
        // 02:00 UTC on 27 Oct is still 26 Oct in New York
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 2, 0, 0, TimeSpan.Zero));

        CreateCalendar(time, "America/New_York").Today.ShouldBe(new DateOnly(2026, 10, 26));
    }
}
