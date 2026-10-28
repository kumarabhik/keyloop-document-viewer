using DocViewer.Api.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DocViewer.Api.Tests;

/// <summary>D-024 and E-012: database trouble is Degraded, and is reported fast enough for the container probe.</summary>
public sealed class DatabaseHealthCheckTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task ConnectThatNeverAnswers_IsDegradedAfterTheTimeout_AndReleasedOnlyWhenItEnds()
    {
        var connect = new TaskCompletionSource<bool>();
        var released = false;

        var result = await DatabaseHealthCheck.ProbeAsync(connect.Task, ShortTimeout, () => released = true, CancellationToken.None);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldBe("Database did not answer within 0.1 s.");
        released.ShouldBeFalse(); // the connect is still running: do not dispose its context yet

        connect.SetException(new TimeoutException("finally gave up"));
        released.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true, HealthStatus.Healthy)]
    [InlineData(false, HealthStatus.Degraded)]
    public async Task ConnectResult_MapsToStatus(bool canConnect, HealthStatus expected)
    {
        var released = false;

        var result = await DatabaseHealthCheck.ProbeAsync(Task.FromResult(canConnect), ShortTimeout, () => released = true, CancellationToken.None);

        result.Status.ShouldBe(expected);
        released.ShouldBeTrue();
    }

    [Fact]
    public async Task ConnectThatThrows_IsDegraded()
    {
        var result = await DatabaseHealthCheck.ProbeAsync(
            Task.FromException<bool>(new InvalidOperationException("refused")), ShortTimeout, () => { }, CancellationToken.None);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Exception.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task CallerCancels_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            DatabaseHealthCheck.ProbeAsync(new TaskCompletionSource<bool>().Task, TimeSpan.FromSeconds(5), () => { }, cts.Token));
    }
}
