namespace BatteryEms.Application.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttMappingConfiguration(
    // ADR 0013 §5.1: mapping-file schema version — leading + required (mirrors OPC-UA).
    string SchemaVersion,
    string ProfileName,
    IReadOnlyList<MqttTopicMapping> Topics);
