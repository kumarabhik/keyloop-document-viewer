namespace DocViewer.Web.Api;

/// <summary>Body of the mocks' <c>POST /__admin/fault</c> (DESIGN.md §7.5).</summary>
internal sealed record FaultRequest(string Mode);
