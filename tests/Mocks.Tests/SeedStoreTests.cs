using Mocks.Common;

namespace Mocks.Tests;

public class SeedStoreTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    [Theory]
    [InlineData("\"{{today}}\"", "yyyy-MM-dd", "\"2026-10-27\"")]
    [InlineData("\"{{today+10d}}\"", "yyyy-MM-dd", "\"2026-11-06\"")]
    [InlineData("\"{{today-20d}}\"", "yyyy-MM-dd", "\"2026-10-07\"")]
    [InlineData("\"{{today+10d}}\"", "dd/MM/yyyy", "\"06/11/2026\"")]
    [InlineData("\"2024-02-11\"", "yyyy-MM-dd", "\"2024-02-11\"")]
    public void ResolveDateTokens_ReplacesRelativeDates(string input, string format, string expected)
    {
        SeedStore.ResolveDateTokens(input, Today, format).ShouldBe(expected);
    }

    [Fact]
    public void ResolveDateTokens_ReplacesEveryOccurrence()
    {
        var json = """{"a":"{{today-1d}}","b":"{{today+1d}}"}""";

        SeedStore.ResolveDateTokens(json, Today, "yyyy-MM-dd").ShouldBe("""{"a":"2026-10-26","b":"2026-10-28"}""");
    }

    [Fact]
    public void SeedData_NamesADealerForEveryDocument()
    {
        // Dealer scoping hides documents without a dealer code (D-034), so the demo data must name one everywhere.
        var root = AppContext.BaseDirectory;
        var sales = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root, "seed", "sales.json")))!.AsObject();
        var service = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root, "seed", "service.json")))!.AsObject();

        sales.Select(v => v.Value!["vehicle"]!["dealerCode"]?.GetValue<string>()).ShouldAllBe(c => !string.IsNullOrWhiteSpace(c));
        service.SelectMany(v => v.Value!["records"]!.AsArray()).Select(r => r!["dealer_code"]?.GetValue<string>())
            .ShouldAllBe(c => !string.IsNullOrWhiteSpace(c));
    }
}
