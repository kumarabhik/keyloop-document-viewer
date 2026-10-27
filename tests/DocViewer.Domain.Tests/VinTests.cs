namespace DocViewer.Domain.Tests;

public class VinTests
{
    [Theory]
    [InlineData("MA3EWB81S00100001")]
    [InlineData("1HGCM82633A004352")]
    [InlineData("WVWZZZ1JZXW000001")]
    public void TryParse_ValidVin_Succeeds(string input)
    {
        Vin.TryParse(input, out var vin, out var error).ShouldBeTrue();

        vin!.Value.ShouldBe(input);
        error.ShouldBeNull();
    }

    [Fact]
    public void TryParse_LowerCase_IsNormalisedToUpperCase()
    {
        Vin.TryParse("ma3ewb81s00100001", out var vin, out _).ShouldBeTrue();

        vin!.Value.ShouldBe("MA3EWB81S00100001");
    }

    [Fact]
    public void TryParse_SurroundingWhitespace_IsTrimmed()
    {
        Vin.TryParse("  MA3EWB81S00100001\t", out var vin, out _).ShouldBeTrue();

        vin!.Value.ShouldBe("MA3EWB81S00100001");
    }

    [Fact]
    public void TryParse_CheckDigitIsNotEnforced()
    {
        // Position 9 ("S") is not a valid North American check digit; still accepted (D-008).
        Vin.TryParse("MA3EWB81S00100001", out _, out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_Missing_ReportsRequired(string? input)
    {
        Vin.TryParse(input, out var vin, out var error).ShouldBeFalse();

        vin.ShouldBeNull();
        error.ShouldBe("VIN is required.");
    }

    [Theory]
    [InlineData("MA3EWB81S0010000", 16)]
    [InlineData("MA3EWB81S001000011", 18)]
    [InlineData("BADVIN", 6)]
    public void TryParse_WrongLength_ReportsActualLength(string input, int length)
    {
        Vin.TryParse(input, out _, out var error).ShouldBeFalse();

        error.ShouldContain("17 characters");
        error.ShouldContain($"Got {length} characters");
    }

    [Theory]
    [InlineData("IA3EWB81S00100001", 'I', 1)]
    [InlineData("MA3EWB81S0010000O", 'O', 17)]
    [InlineData("MA3EWQ81S00100001", 'Q', 6)]
    [InlineData("ma3ewb81s0010000o", 'O', 17)]
    public void TryParse_ForbiddenLetter_ReportsLetterAndPosition(string input, char letter, int position)
    {
        Vin.TryParse(input, out _, out var error).ShouldBeFalse();

        error.ShouldContain($"'{letter}'");
        error.ShouldContain($"position {position}");
    }

    [Theory]
    [InlineData("MA3EWB81S0010000-", '-', 17)]
    [InlineData("MA3 WB81S00100001", ' ', 4)]
    [InlineData("MA3ÉWB81S0010000", 'É', 4)]
    public void TryParse_NonAlphanumeric_ReportsCharacterAndPosition(string input, char character, int position)
    {
        Vin.TryParse(input.PadRight(17, '1'), out _, out var error).ShouldBeFalse();

        error.ShouldContain($"'{character}'");
        error.ShouldContain($"position {position}");
    }

    [Fact]
    public void Masked_ShowsOnlyWmiAndLastFour()
    {
        Vin.Parse("MA3EWB81S00100001").Masked.ShouldBe("MA3**********0001");
    }

    [Fact]
    public void Parse_Invalid_Throws()
    {
        Should.Throw<FormatException>(() => Vin.Parse("BADVIN"));
    }

    [Fact]
    public void Equality_IsByValueAfterNormalisation()
    {
        Vin.Parse("ma3ewb81s00100001").ShouldBe(Vin.Parse("MA3EWB81S00100001"));
    }
}
