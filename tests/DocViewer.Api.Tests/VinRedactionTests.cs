using DocViewer.Api.Observability;

namespace DocViewer.Api.Tests;

public class VinRedactionTests
{
    [Theory]
    [InlineData("/api/v1/vehicles/MA3EWB81S00100001/documents", "/api/v1/vehicles/MA3**********0001/documents")]
    [InlineData("http://sales:8080/sales/v1/vehicles/ma3ewb81s00100001/documents", "http://sales:8080/sales/v1/vehicles/ma3**********0001/documents")]
    [InlineData("/api/v1/vehicles/MA3EWB81S0010000O/documents", "/api/v1/vehicles/MA3**********000O/documents")] // invalid, still masked
    [InlineData("vin=MA3EWB81S00100001", "vin=MA3**********0001")]
    public void Redact_MasksVinShapedTokens(string input, string expected) =>
        VinRedactionProcessor.Redact(input).ShouldBe(expected);

    [Theory]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e4736")] // trace id: 32 chars, not a VIN
    [InlineData("/api/v1/vehicles/{vin}/documents")]
    [InlineData("MA3EWB81S0010000")] // 16 chars
    public void Redact_LeavesOtherValuesUntouched(string input) =>
        VinRedactionProcessor.Redact(input).ShouldBeSameAs(input);
}
