namespace Mocks.Common;

/// <summary>Failure behaviours a mock can be switched into for demos and tests (DESIGN.md §7.5).</summary>
public enum FaultMode
{
    /// <summary>Behave normally.</summary>
    None,

    /// <summary>Add <c>DelayMs</c> (default 800 ms) and then respond normally.</summary>
    Slow,

    /// <summary>Wait <c>DelayMs</c> (default 5 s, beyond the aggregator's 2 s budget) before responding.</summary>
    Timeout,

    /// <summary>Respond 500 to every request.</summary>
    Error,

    /// <summary>Respond 503 to a <c>Rate</c> share of requests (default 0.5).</summary>
    Flaky,

    /// <summary>Respond 200 with a truncated, unparseable JSON body.</summary>
    Malformed,
}
