using Bunit;
using DocViewer.Web.Api;
using DocViewer.Web.Pages;
using Microsoft.Extensions.DependencyInjection;
using Vins = DocViewer.Web.Api.MockApiHandler.Vins;

namespace DocViewer.Web.Tests.Pages;

/// <summary>
/// The whole page against mock mode (D4.5), which serves every state from <see cref="MockApiHandler"/>:
/// D4.2 deep link and history, D4.3 results, D4.4 states, D5.1 expiry alert, D5.2 health panel, D5.3 URL filters.
/// </summary>
public sealed class HomePageTests : IDisposable
{
    private readonly CountingHandler _calls = new();
    private readonly BunitContext _ctx;

    public HomePageTests() =>
        _ctx = WebTest.Create(services => services.AddHttpClient<DocumentsApiClient>().AddHttpMessageHandler(() => _calls));

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<Home> Open(string query)
    {
        _ctx.NavigateTo($"/{query}");
        return _ctx.Render<Home>();
    }

    private static IRenderedComponent<Home> WaitForState(IRenderedComponent<Home> cut, string state)
    {
        cut.WaitForElement($"[data-state='{state}']", TimeSpan.FromSeconds(5));
        return cut;
    }

    [Fact]
    public void DeepLink_SearchesAndShowsEveryDocumentWithItsSource()
    {
        var cut = WaitForState(Open($"?vin={Vins.Complete}"), "complete");

        var rows = cut.FindAll("[data-document]");
        rows.Count.ShouldBe(6);
        rows.ShouldAllBe(r => r.QuerySelector(".badge") != null);
        cut.FindAll("[data-document] .badge--sales").Count.ShouldBe(3);
        cut.FindAll("[data-document] .badge--service").Count.ShouldBe(3);
        cut.Find("[data-trace] code").TextContent.ShouldNotBeNullOrWhiteSpace();
        cut.FindAll("[data-banner]").ShouldBeEmpty();
    }

    [Fact]
    public void DeepLink_LowerCaseVin_IsNormalised()
    {
        var cut = WaitForState(Open($"?vin={Vins.Complete.ToLowerInvariant()}"), "complete");

        cut.Find("input").GetAttribute("value").ShouldBe(Vins.Complete);
    }

    [Fact]
    public void DeepLink_InvalidVin_ShowsTheHintAndCallsNothing()
    {
        var cut = Open("?vin=BADVIN");

        cut.Find("[data-hint]").TextContent.ShouldContain("Got 6 characters");
        cut.FindAll("[data-state]").ShouldBeEmpty();
        _calls.Searches.ShouldBe(0);
    }

    [Fact]
    public void Partial_ShowsABannerNamingTheFailedSystem()
    {
        var cut = WaitForState(Open($"?vin={Vins.Partial}"), "partial");

        var banner = cut.Find("[data-banner='partial']");
        banner.GetAttribute("data-source").ShouldBe(WireValues.Service);
        cut.FindAll("[data-document] .badge--service").ShouldBeEmpty();
        cut.FindAll("[data-document] .badge--sales").Count.ShouldBe(3);
    }

    [Fact]
    public void Stale_ShowsTheSavedCopyWithItsTime()
    {
        var cut = WaitForState(Open($"?vin={Vins.Stale}"), "partial");

        cut.Find("[data-banner='stale']").TextContent.ShouldContain("28 Oct 2026, 07:00");
        cut.FindAll("[data-document] .badge--service").Count.ShouldBe(3);
    }

    [Fact]
    public void Unavailable_ShowsBothFailuresAndARetry()
    {
        var cut = WaitForState(Open($"?vin={Vins.Unavailable}"), "unavailable");

        cut.FindAll("[data-state='unavailable'] li").Count.ShouldBe(2);
        cut.Find("[data-state='unavailable']").TextContent.ShouldContain("Suggested wait: 5 seconds");
        cut.Find("[data-trace]").ShouldNotBeNull();

        cut.Find("[data-state='unavailable'] button").Click();
        cut.WaitForAssertion(() => _calls.Searches.ShouldBe(2));
    }

    [Fact]
    public void Empty_ShowsTheEmptyStateNotAnError()
    {
        var cut = WaitForState(Open($"?vin={Vins.Empty}"), "empty");

        cut.Find("[data-state='empty']").TextContent.ShouldContain("neither holds any documents");
        cut.FindAll("[role='alert']").ShouldBeEmpty();
    }

    [Fact]
    public void Expiring_AlertCountsAndFiltersByStatus()
    {
        var cut = WaitForState(Open($"?vin={Vins.Expiring}"), "complete");

        cut.Find(".alert__text").TextContent.ShouldBe("1 expired, 1 expiring in 10 days");
        cut.Find("[data-alert] button").Click(); // "Show expired"

        _ctx.CurrentUri().ShouldContain("status=EXPIRED");
    }

    [Fact]
    public void Conflict_FlagsBothRecords()
    {
        var cut = WaitForState(Open($"?vin={Vins.Conflict}"), "complete");

        cut.FindAll(".flag--conflict").Count.ShouldBe(2);
        cut.Find("[data-summary]").TextContent.ShouldContain("2 conflicts");
    }

    [Fact]
    public void FilterFromUrl_NarrowsTheTableButNotTheCounts()
    {
        var cut = WaitForState(Open($"?vin={Vins.Complete}&source=SALES&type=insurance"), "complete");

        cut.FindAll("[data-document]").Count.ShouldBe(1);
        cut.Find("[data-count]").TextContent.ShouldBe("Showing 1 of 6");
        cut.Find("[data-summary]").TextContent.ShouldContain("6 documents");
        cut.Find("[data-filter='type']").GetAttribute("value").ShouldBe("INSURANCE");
    }

    [Fact]
    public void ChangingAFilter_UpdatesTheUrlWithoutSearchingAgain()
    {
        var cut = WaitForState(Open($"?vin={Vins.Complete}"), "complete");

        cut.Find("[data-filter='source']").Change(WireValues.Service);

        _ctx.CurrentUri().ShouldContain("source=SERVICE");
        _ctx.CurrentUri().ShouldContain($"vin={Vins.Complete}");
        cut.WaitForAssertion(() => cut.FindAll("[data-document]").Count.ShouldBe(3));
        _calls.Searches.ShouldBe(1);
    }

    [Fact]
    public void FilterHidingEverything_OffersToClear()
    {
        var cut = WaitForState(Open($"?vin={Vins.Complete}&type=RC"), "complete");

        cut.Find("[data-state='no-match'] button").Click();

        _ctx.CurrentUri().ShouldNotContain("type=");
    }

    [Fact]
    public void Search_NewVin_NavigatesAndClearsFilters()
    {
        var cut = WaitForState(Open($"?vin={Vins.Complete}&source=SALES"), "complete");

        cut.Find("input").Input(Vins.Conflict);
        cut.Find("form").Submit();

        _ctx.CurrentUri().ShouldContain($"vin={Vins.Conflict}");
        _ctx.CurrentUri().ShouldNotContain("source=");
    }

    [Fact]
    public void SuccessfulSearch_IsSavedToRecentSearches()
    {
        WaitForState(Open($"?vin={Vins.Complete}"), "complete");

        var saved = _ctx.JSInterop.Invocations.Where(i => i.Identifier == "localStorage.setItem").ShouldHaveSingleItem();
        saved.Arguments[1]!.ToString()!.ShouldContain(Vins.Complete);
    }

    [Fact]
    public void HealthPanel_FaultToggle_MakesTheNextSearchPartial()
    {
        var cut = WaitForState(Open($"?vin={Vins.Complete}"), "complete");
        cut.WaitForElement("[data-fault='SERVICE']");

        cut.Find("[data-fault='SERVICE']").Change("timeout");
        cut.WaitForAssertion(() => cut.Find("[data-health-message]").TextContent.ShouldContain("Hang (timeout)"));
        cut.Find(".results-section__head button").Click(); // Refresh

        WaitForState(cut, "partial").Find("[data-banner='partial']").GetAttribute("data-source").ShouldBe(WireValues.Service);
        cut.WaitForAssertion(() => cut.Find("[data-health-source='SERVICE'] .circuit").TextContent.ShouldContain("Open"));
    }

    /// <summary>Counts document searches that reach the (mock) API.</summary>
    private sealed class CountingHandler : DelegatingHandler
    {
        private int _searches;

        public int Searches => Volatile.Read(ref _searches);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/documents", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _searches);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
