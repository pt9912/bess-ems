namespace BatteryEms.Application.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttMappingConfiguration(
    string ProfileName,
    IReadOnlyList<MqttTopicMapping> Topics);
