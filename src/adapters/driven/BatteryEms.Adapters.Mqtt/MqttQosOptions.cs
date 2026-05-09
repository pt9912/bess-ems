namespace BatteryEms.Adapters.Mqtt;

// Per-channel QoS configuration for the MQTT adapter (RM-M4-06).
// Defaults match D-03: AtLeastOnce for command publish + command-ACK
// subscribe (commands and their acknowledgements must not be lost);
// AtMostOnce for telemetry subscribe (stream-like data where the next
// sample arrives before the loss matters).
//
// Status and fault subscribe slots are intentionally NOT carried here:
// MqttTelemetrySource currently consumes the `telemetry` topic only
// (it documents its own M1 reasoning at the source). When a separate
// status/fault subscriber lands, this record gets the matching slots
// at that point — leaving unwired knobs on a public record now would
// invite operators to set values that no consumer reads.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttQosOptions(
    MqttQualityOfService CommandPublish = MqttQualityOfService.AtLeastOnce,
    MqttQualityOfService CommandAckSubscribe = MqttQualityOfService.AtLeastOnce,
    MqttQualityOfService TelemetrySubscribe = MqttQualityOfService.AtMostOnce)
{
    public static MqttQosOptions Defaults => new();
}
