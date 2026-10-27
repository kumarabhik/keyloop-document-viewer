using System.Text.Json;

namespace DocViewer.Domain.Tests;

public class VinJsonConverterTests
{
    [Fact]
    public void Serialize_WritesPlainString()
    {
        JsonSerializer.Serialize(Vin.Parse("MA3EWB81S00100001")).ShouldBe("\"MA3EWB81S00100001\"");
    }

    [Fact]
    public void Deserialize_NormalisesAndValidates()
    {
        JsonSerializer.Deserialize<Vin>("\" ma3ewb81s00100001 \"").ShouldBe(Vin.Parse("MA3EWB81S00100001"));
    }

    [Theory]
    [InlineData("\"BADVIN\"")]
    [InlineData("\"MA3EWB81S0010000O\"")]
    [InlineData("\"\"")]
    public void Deserialize_Invalid_Throws(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Vin>(json));
    }

    [Fact]
    public void Deserialize_JsonNull_IsNull()
    {
        // System.Text.Json does not invoke converters for null tokens on reference types (HandleNull = false).
        JsonSerializer.Deserialize<Vin>("null").ShouldBeNull();
    }

    [Fact]
    public void Document_RoundTripsThroughJson()
    {
        var original = TestDocuments.Create("INV-1", expiry: "2027-01-01", reference: "R-1");

        var copy = JsonSerializer.Deserialize<Document>(JsonSerializer.Serialize(original))!;

        copy.Id.ShouldBe(original.Id);
        copy.Vin.ShouldBe(original.Vin);
        copy.ExpiryDate.ShouldBe(original.ExpiryDate);
        copy.ReferenceNumber.ShouldBe("R-1");
    }
}
