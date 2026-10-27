namespace DocViewer.Domain.Tests;

public class DocumentStatusCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    [Theory]
    [InlineData(-365, DocumentStatus.Expired)]
    [InlineData(-1, DocumentStatus.Expired)]
    [InlineData(0, DocumentStatus.ExpiringSoon)]
    [InlineData(10, DocumentStatus.ExpiringSoon)]
    [InlineData(30, DocumentStatus.ExpiringSoon)]
    [InlineData(31, DocumentStatus.Valid)]
    [InlineData(400, DocumentStatus.Valid)]
    public void Calculate_RelativeToToday(int daysFromToday, DocumentStatus expected)
    {
        DocumentStatusCalculator.Calculate(Today.AddDays(daysFromToday), Today, warningDays: 30).ShouldBe(expected);
    }

    [Fact]
    public void Calculate_NoExpiry_IsNotApplicable()
    {
        DocumentStatusCalculator.Calculate(null, Today, 30).ShouldBe(DocumentStatus.NotApplicable);
    }

    [Fact]
    public void Calculate_ZeroWarningDays_OnlyTodayIsExpiringSoon()
    {
        DocumentStatusCalculator.Calculate(Today, Today, 0).ShouldBe(DocumentStatus.ExpiringSoon);
        DocumentStatusCalculator.Calculate(Today.AddDays(1), Today, 0).ShouldBe(DocumentStatus.Valid);
    }

    [Fact]
    public void Calculate_NegativeWarningDays_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DocumentStatusCalculator.Calculate(Today, Today, -1));
    }
}
