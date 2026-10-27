using System.Text.Json.Serialization;

namespace Mocks.Common;

/// <summary>Body of <c>POST /__admin/fault</c> and response of <c>GET /__admin/fault</c>.</summary>
public sealed record FaultSettings(FaultMode Mode = FaultMode.None, int? DelayMs = null, double? Rate = null)
{
    public static readonly FaultSettings Healthy = new();

    [JsonIgnore]
    public TimeSpan EffectiveDelay => TimeSpan.FromMilliseconds(DelayMs ?? Mode switch
    {
        FaultMode.Slow => 800,
        FaultMode.Timeout => 5000,
        _ => 0,
    });

    [JsonIgnore]
    public double EffectiveRate => Rate ?? 0.5;

    public string? Validate() => this switch
    {
        { DelayMs: < 0 or > 60_000 } => "delayMs must be between 0 and 60000.",
        { Rate: < 0 or > 1 } => "rate must be between 0 and 1.",
        _ when !Enum.IsDefined(Mode) => "Unknown mode.",
        _ => null,
    };
}
