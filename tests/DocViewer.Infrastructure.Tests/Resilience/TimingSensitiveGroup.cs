namespace DocViewer.Infrastructure.Tests.Resilience;

/// <summary>
/// Tests that assert on real elapsed time run alone, so CPU contention from other test classes
/// (e.g. WireMock responses with multi-second delays) cannot push them over a timeout (E-001).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingSensitiveGroup
{
    public const string Name = "Timing-sensitive";
}
