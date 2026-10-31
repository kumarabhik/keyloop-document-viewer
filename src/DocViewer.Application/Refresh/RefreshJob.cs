using DocViewer.Domain;

namespace DocViewer.Application.Refresh;

/// <summary>One background refresh of a VIN; <paramref name="Attempt"/> starts at 1.</summary>
public sealed record RefreshJob(Vin Vin, int Attempt);
