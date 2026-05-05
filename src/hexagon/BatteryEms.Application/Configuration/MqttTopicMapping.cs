namespace BatteryEms.Application.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttTopicMapping(
    string Name,
    string Topic,
    string Direction,
    string PayloadFormat,
    bool Retained,
    string AuthRequired);
