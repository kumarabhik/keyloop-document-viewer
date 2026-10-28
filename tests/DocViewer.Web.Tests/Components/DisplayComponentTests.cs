using Bunit;
using DocViewer.Web.Api;
using DocViewer.Web.Components;

namespace DocViewer.Web.Tests.Components;

/// <summary>D4.3 and D4.4: badges, chips, flags and banners.</summary>
public sealed class DisplayComponentTests : IDisposable
{
    private readonly BunitContext _ctx = WebTest.Create();

    public void Dispose() => _ctx.Dispose();

    [Theory]
    [InlineData(WireValues.Sales, "badge--sales", "Sales")]
    [InlineData(WireValues.Service, "badge--service", "Service")]
    public void SourceBadge_IsColouredAndNamedPerSource(string source, string cssClass, string text)
    {
        var badge = _ctx.Render<SourceBadge>(p => p.Add(x => x.Source, source)).Find(".badge");

        badge.ClassList.ShouldContain(cssClass);
        badge.TextContent.ShouldBe(text);
    }

    [Theory]
    [InlineData(WireValues.Expired, -21, "chip--expired", "Expired · expired 21 days ago")]
    [InlineData(WireValues.ExpiringSoon, 10, "chip--expiring", "Expiring soon · expires in 10 days")]
    [InlineData(WireValues.ExpiringSoon, 0, "chip--expiring", "Expiring soon · expires today")]
    [InlineData(WireValues.Valid, 400, "chip--valid", "Valid")]
    public void StatusChip_ShowsStatusAndDaysLeft(string status, int daysFromToday, string cssClass, string text)
    {
        var chip = _ctx.Render<StatusChip>(p => p
            .Add(x => x.Status, status)
            .Add(x => x.ExpiryDate, WebTest.Today.AddDays(daysFromToday))
            .Add(x => x.Today, WebTest.Today)).Find(".chip");

        chip.ClassList.ShouldContain(cssClass);
        chip.TextContent.ShouldBe(text);
    }

    [Fact]
    public void StatusChip_NoExpiry_SaysSo()
    {
        _ctx.Render<StatusChip>(p => p.Add(x => x.Status, WireValues.NotApplicable).Add(x => x.Today, WebTest.Today))
            .Find(".chip").TextContent.ShouldBe("No expiry");
    }

    [Fact]
    public void FlagIcons_ExplainDuplicatesAndConflictsInTooltips()
    {
        var cut = _ctx.Render<FlagIcons>(p => p.Add(x => x.Flags, [WireValues.Conflict, WireValues.PossibleDuplicate, "STALE"]));

        var flags = cut.FindAll(".flag");
        flags.Count.ShouldBe(2); // unknown markers are not shown
        flags[0].GetAttribute("title")!.ShouldContain("disagree");
        flags[1].GetAttribute("title")!.ShouldContain("both systems");
    }

    [Fact]
    public void SourceBanners_Partial_NamesTheFailedAndTheAnsweringSystem()
    {
        var cut = _ctx.Render<SourceBanners>(p => p.Add(x => x.Sources,
        [
            new SourceStatusResponse(WireValues.Sales, "ok", 100, 3, null, null),
            new SourceStatusResponse(WireValues.Service, "timeout", 2001, 0, null, "timeout"),
        ]));

        var banner = cut.Find("[data-banner]");
        banner.GetAttribute("data-banner").ShouldBe("partial");
        banner.GetAttribute("data-source").ShouldBe(WireValues.Service);
        banner.TextContent.ShouldContain("Service system did not answer in time.");
        banner.TextContent.ShouldContain("from the Sales system only");
    }

    [Fact]
    public void SourceBanners_Stale_ShowsWhenTheSavedCopyIsFrom()
    {
        var cut = _ctx.Render<SourceBanners>(p => p.Add(x => x.Sources,
        [
            new SourceStatusResponse(WireValues.Sales, "ok", 100, 3, null, null),
            new SourceStatusResponse(WireValues.Service, "stale", 2001, 5, WebTest.Now.AddHours(-3), "timeout"),
        ]));

        var banner = cut.Find("[data-banner]");
        banner.GetAttribute("data-banner").ShouldBe("stale");
        banner.TextContent.ShouldContain("last saved copy from 28 Oct 2026, 07:00");
    }

    [Fact]
    public void SourceBanners_AllOk_RendersNothing()
    {
        _ctx.Render<SourceBanners>(p => p.Add(x => x.Sources, [new SourceStatusResponse(WireValues.Sales, "ok", 1, 1, null, null)]))
            .Markup.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void ExpiryAlert_CountsExpiredAndDaysToTheNextExpiry()
    {
        string? shown = null;
        var cut = _ctx.Render<ExpiryAlert>(p => p
            .Add(x => x.Documents, [Doc(WireValues.Expired, -21), Doc(WireValues.ExpiringSoon, 10), Doc(WireValues.Valid, 300)])
            .Add(x => x.Today, WebTest.Today)
            .Add(x => x.OnShowStatus, status => shown = status));

        cut.Find(".alert__text").TextContent.ShouldBe("1 expired, 1 expiring in 10 days");
        cut.FindAll("button")[0].Click();
        shown.ShouldBe(WireValues.Expired);
    }

    [Fact]
    public void ExpiryAlert_SeveralExpiring_NamesTheSoonest()
    {
        var cut = _ctx.Render<ExpiryAlert>(p => p
            .Add(x => x.Documents, [Doc(WireValues.ExpiringSoon, 25), Doc(WireValues.ExpiringSoon, 1)])
            .Add(x => x.Today, WebTest.Today));

        cut.Find(".alert__text").TextContent.ShouldBe("2 expiring soon (next tomorrow)");
    }

    [Fact]
    public void ExpiryAlert_NothingExpiring_RendersNothing()
    {
        _ctx.Render<ExpiryAlert>(p => p.Add(x => x.Documents, [Doc(WireValues.Valid, 90)]).Add(x => x.Today, WebTest.Today))
            .Markup.Trim().ShouldBeEmpty();
    }

    private static DocumentResponse Doc(string status, int expiresInDays) =>
        new($"SALES:{status}{expiresInDays}", "X", "MA3EWB81S00100005", "Doc", "INSURANCE", WebTest.Today.AddYears(-1),
            WebTest.Today.AddDays(expiresInDays), WireValues.Sales, status, [], new Dictionary<string, string>());
}
