using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace DocViewer.Api.Tests;

public sealed class ContainerHealthCheckTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() => _server.Dispose();

    private Uri Readyz => new($"{_server.Url}/readyz");

    /// <summary>
    /// Not the 3 s production timeout: on the first run after a build every WireMock request took over 3 s, so
    /// 200 failed and 503 "passed" only because it timed out (E-011). These tests check status mapping, not timing.
    /// </summary>
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(200, 0)]
    [InlineData(503, 1)]
    public async Task Probe_ExitCodeFollowsStatus(int status, int expectedExitCode)
    {
        _server.Given(Request.Create().WithPath("/readyz").UsingGet()).RespondWith(Response.Create().WithStatusCode(status));

        (await ContainerHealthCheck.ProbeAsync(Readyz, GenerousTimeout)).ShouldBe(expectedExitCode);
        _server.LogEntries.ShouldHaveSingleItem().ResponseMessage.ShouldNotBeNull().StatusCode.ShouldBe(status); // a real answer, not a timeout
    }

    [Fact]
    public async Task Probe_TooSlow_IsUnhealthy()
    {
        _server.Given(Request.Create().WithPath("/readyz").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(2)));

        (await ContainerHealthCheck.ProbeAsync(Readyz, TimeSpan.FromMilliseconds(300))).ShouldBe(1);
    }

    [Fact]
    public async Task Probe_NothingListening_IsUnhealthy()
    {
        var url = Readyz;
        _server.Stop();

        (await ContainerHealthCheck.ProbeAsync(url, TimeSpan.FromSeconds(3))).ShouldBe(1);
    }

    [Theory]
    [InlineData(null, "8080")]
    [InlineData("", "8080")] // used to build http://localhost:/readyz, i.e. port 80
    [InlineData("5000", "5000")]
    [InlineData(" 5000 ; 5001", "5000")]
    [InlineData("abc,9000", "9000")]
    public void PortFrom_FirstNumericEntryElse8080(string? httpPorts, string expected) =>
        ContainerHealthCheck.PortFrom(httpPorts).ShouldBe(expected);

    [Theory]
    [InlineData(new[] { "--healthcheck" }, true)]
    [InlineData(new[] { "--healthcheck", "/healthz" }, true)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--urls", "http://+:8080" }, false)]
    public void IsRequested_OnlyForTheFlagAsFirstArgument(string[] args, bool expected) =>
        ContainerHealthCheck.IsRequested(args).ShouldBe(expected);
}
