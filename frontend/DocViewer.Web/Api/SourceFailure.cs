namespace DocViewer.Web.Api;

/// <summary>Per-source entry in the 503 problem's <c>sources</c> extension.</summary>
public sealed record SourceFailure(string Name, string Status, string? Error);
