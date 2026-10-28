namespace DocViewer.Web.Api;

/// <summary>What a search ended as. Expected API outcomes are values, not exceptions (AGENTS.md §6).</summary>
internal abstract record SearchOutcome
{
    /// <summary>200: complete, partial, stale or empty (D-004).</summary>
    public sealed record Found(DocumentsResponse Response) : SearchOutcome;

    /// <summary>400: invalid VIN or filter; <see cref="ProblemResponse.Detail"/> says what is wrong.</summary>
    public sealed record Rejected(ProblemResponse Problem) : SearchOutcome;

    /// <summary>503: both sources failed and no snapshot exists.</summary>
    public sealed record Unavailable(ProblemResponse Problem, TimeSpan RetryAfter) : SearchOutcome;

    /// <summary>The API could not be reached, or answered something unexpected.</summary>
    public sealed record Failed(string Message, string? TraceId) : SearchOutcome;
}
