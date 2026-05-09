namespace BatteryEms.Application.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OpcUaMappingConfiguration(
    string SchemaVersion,
    string ProfileName,
    IReadOnlyList<OpcUaNodeMapping> Nodes);
