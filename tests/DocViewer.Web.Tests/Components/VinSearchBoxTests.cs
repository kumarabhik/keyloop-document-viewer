using Bunit;
using DocViewer.Domain;
using DocViewer.Web.Components;

namespace DocViewer.Web.Tests.Components;

/// <summary>D4.2: live hints come from the same <see cref="Vin.TryParse"/> rules the API enforces.</summary>
public sealed class VinSearchBoxTests : IDisposable
{
    private readonly BunitContext _ctx = WebTest.Create();

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<VinSearchBox> Render(string value, Action<Vin>? onSearch = null, IReadOnlyList<string>? recent = null, string? serverError = null) =>
        _ctx.Render<VinSearchBox>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.OnSearch, vin => onSearch?.Invoke(vin))
            .Add(x => x.Recent, recent ?? [])
            .Add(x => x.ServerError, serverError));

    [Theory]
    [InlineData("", "17 characters")]
    [InlineData("MA3EWB81S0010000", "Got 16 characters")]
    [InlineData("MA3EWB81S0010000O", "cannot contain 'O'")]
    [InlineData("MA3EWB81S0010000#", "invalid character '#'")]
    [InlineData("MA3EWB81S00100001", "Valid VIN")]
    public void Hint_ExplainsWhatIsWrong(string value, string expected)
    {
        Render(value).Find("[data-hint]").TextContent.ShouldContain(expected);
    }

    [Theory]
    [InlineData("MA3EWB81S0010000", true)]
    [InlineData("MA3EWB81S00100001", false)]
    [InlineData(" ma3ewb81s00100001 ", false)] // valid after trimming and upper-casing
    public void SearchButton_IsEnabledOnlyForAValidVin(string value, bool disabled)
    {
        Render(value).Find("button[type=submit]").HasAttribute("disabled").ShouldBe(disabled);
    }

    [Fact]
    public void Submit_PassesTheNormalisedVin()
    {
        Vin? searched = null;
        Render(" ma3ewb81s00100001 ", v => searched = v).Find("form").Submit();

        searched.ShouldBe(Vin.Parse("MA3EWB81S00100001"));
    }

    [Fact]
    public void Submit_InvalidVin_DoesNotSearch()
    {
        var searched = false;
        Render("BADVIN", _ => searched = true).Find("form").Submit();

        searched.ShouldBeFalse();
    }

    [Fact]
    public void Typing_UpperCasesTheValue()
    {
        string? changed = null;
        var cut = _ctx.Render<VinSearchBox>(p => p
            .Add(x => x.ValueChanged, v => changed = v)
            .Add(x => x.OnSearch, _ => { }));

        cut.Find("input").Input("ma3ewb");

        changed.ShouldBe("MA3EWB");
    }

    [Fact]
    public void RecentSearch_ClickSearchesThatVin()
    {
        Vin? searched = null;
        var cut = Render("", v => searched = v, recent: ["MA3EWB81S00100005"]);

        cut.Find("[data-recent] button").Click();

        searched!.Value.ShouldBe("MA3EWB81S00100005");
    }

    [Fact]
    public void ServerError_ReplacesTheHintAndMarksTheInputInvalid()
    {
        var cut = Render("MA3EWB81S00100001", serverError: "VIN cannot contain 'O'.");

        cut.Find("[data-hint]").TextContent.ShouldBe("VIN cannot contain 'O'.");
        cut.Find("input").GetAttribute("aria-invalid").ShouldBe("true");
    }
}
