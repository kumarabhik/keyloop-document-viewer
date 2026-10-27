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
}
