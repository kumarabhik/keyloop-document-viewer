using DocViewer.Domain;

namespace DocViewer.Application.Sources;

public sealed record SourceHealth(SourceSystem Source, CircuitState Circuit);
