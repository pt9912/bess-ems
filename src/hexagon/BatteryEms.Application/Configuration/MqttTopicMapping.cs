namespace BatteryEms.Application.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttTopicMapping(
    string Name,
    string Topic,
    string Direction,
    string PayloadFormat,
    bool Retained,
    string AuthRequired)
{
    // LH-DOM-005 device-point metadata: optional, init-only so existing
    // call sites that build the record positionally stay source-compatible.
    public DevicePointMetadata? DevicePoint { get; init; }
}
