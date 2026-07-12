namespace BatteryEms.Application.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record ModbusMappingConfiguration(
    // ADR 0013 §5.1: mapping-file schema version — leading + required, mirroring
    // OpcUaMappingConfiguration. The loader sets it from the file; construction
    // sites must supply it (no silent "v1" default that could mask the real version).
    string SchemaVersion,
    string ProfileName,
    string UnitIdDiscovery,
    int? StaticUnitId,
    IReadOnlyList<ModbusRegisterMapping> Registers);
