namespace BatteryEms.Application.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttMappingConfiguration(
    string ProfileName,
    IReadOnlyList<MqttTopicMapping> Topics,
    // ADR 0013 §5.1: mapping-file schema version (trailing default; loader sets it).
    string SchemaVersion = "v1");
