namespace BatteryEms.Adapters.Mqtt;

// Per-channel QoS configuration for the MQTT adapter (RM-M4-06).
// Defaults match D-03: AtLeastOnce for command publish, command-ACK
// subscribe, status subscribe and fault subscribe (state changes
// must not be lost); AtMostOnce for telemetry subscribe (stream-
// like data where the next sample arrives before the loss matters).
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttQosOptions(
    MqttQualityOfService CommandPublish = MqttQualityOfService.AtLeastOnce,
    MqttQualityOfService CommandAckSubscribe = MqttQualityOfService.AtLeastOnce,
    MqttQualityOfService TelemetrySubscribe = MqttQualityOfService.AtMostOnce,
    MqttQualityOfService StatusSubscribe = MqttQualityOfService.AtLeastOnce,
    MqttQualityOfService FaultSubscribe = MqttQualityOfService.AtLeastOnce)
{
    public static MqttQosOptions Defaults => new();
}
