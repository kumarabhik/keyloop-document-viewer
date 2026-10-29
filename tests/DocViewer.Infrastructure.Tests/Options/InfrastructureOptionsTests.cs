using System.ComponentModel.DataAnnotations;
using DocViewer.Infrastructure.Options;

namespace DocViewer.Infrastructure.Tests.Options;

public class InfrastructureOptionsTests
{
    private static List<ValidationResult> Validate(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        // DataAnnotations skips IValidatableObject when attribute validation fails; run it explicitly.
        if (results.Count == 0 && options is IValidatableObject validatable)
        {
            results.AddRange(validatable.Validate(new ValidationContext(options)));
        }

        return results;
    }

    [Fact]
    public void ResilienceDefaults_MatchD005AndD014()
    {
        var options = new ResilienceOptions();

        options.TotalTimeout.ShouldBe(TimeSpan.FromSeconds(2));
        options.AttemptTimeout.ShouldBe(TimeSpan.FromSeconds(1.5));
        options.ConnectTimeout.ShouldBe(TimeSpan.FromSeconds(0.5));
        options.MaxRetryAttempts.ShouldBe(1);
        options.BreakerFailureRatio.ShouldBe(0.5);
        options.BreakerMinimumThroughput.ShouldBe(3); // D-027
        options.BreakerBreakDuration.ShouldBe(TimeSpan.FromSeconds(30));
        Validate(options).ShouldBeEmpty();
    }

    [Fact]
    public void Resilience_AttemptTimeoutLongerThanTotal_IsInvalid()
    {
        var options = new ResilienceOptions { TotalTimeoutSeconds = 1, AttemptTimeoutSeconds = 1.5 };

        Validate(options).ShouldContain(r => r.MemberNames.Contains(nameof(ResilienceOptions.AttemptTimeoutSeconds)));
    }

    [Fact]
    public void Resilience_TooManyRetries_IsInvalid()
    {
        Validate(new ResilienceOptions { MaxRetryAttempts = 5 }).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    public void SourceEndpoint_RequiresAbsoluteUrl(string baseUrl)
    {
        Validate(new SourceEndpointOptions { BaseUrl = baseUrl }).ShouldNotBeEmpty();
    }

    [Fact]
    public void SourceEndpoint_ValidUrl_IsValid()
    {
        Validate(new SourceEndpointOptions { BaseUrl = "http://localhost:8081" }).ShouldBeEmpty();
    }

    [Fact]
    public void Persistence_RequiresSaltOfAtLeastEightCharacters()
    {
        Validate(new PersistenceOptions { VinHashSalt = "short" }).ShouldNotBeEmpty();
        Validate(new PersistenceOptions { VinHashSalt = "long-enough-salt" }).ShouldBeEmpty();
    }
}
