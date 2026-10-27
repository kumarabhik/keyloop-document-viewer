using DocViewer.Application.Sources;
using DocViewer.Domain;
using Microsoft.Extensions.Logging;

namespace DocViewer.Infrastructure.Sources;

/// <summary>Source-generated log messages for adapters. Only masked VINs are logged (D-011).</summary>
internal static partial class SourceLog
{
    [LoggerMessage(EventId = 1100, Level = LogLevel.Information,
        Message = "source.fetched {Source} vin={VinMasked} documents={DocumentCount} dropped={DroppedCount} latencyMs={LatencyMs}")]
    public static partial void Fetched(ILogger logger, SourceSystem source, string vinMasked, int documentCount, int droppedCount, double latencyMs);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning,
        Message = "source.failed {Source} vin={VinMasked} status={Status} error={Error} latencyMs={LatencyMs}")]
    public static partial void Failed(ILogger logger, SourceSystem source, string vinMasked, SourceStatus status, string error, double latencyMs, Exception exception);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning,
        Message = "source.record_dropped {Source} recordId={RecordId} reason={Reason}")]
    public static partial void RecordDropped(ILogger logger, SourceSystem source, string recordId, string reason);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information,
        Message = "source.unknown_type {Source} recordId={RecordId} type={RawType}")]
    public static partial void UnknownType(ILogger logger, SourceSystem source, string recordId, string rawType);
}
