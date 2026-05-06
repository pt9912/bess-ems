namespace BatteryEms.Adapters.Mqtt;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttMessage(string Topic, ReadOnlyMemory<byte> Payload);
