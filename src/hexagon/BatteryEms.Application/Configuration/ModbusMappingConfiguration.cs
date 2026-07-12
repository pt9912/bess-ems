namespace BatteryEms.Application.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record ModbusMappingConfiguration(
    string ProfileName,
    string UnitIdDiscovery,
    int? StaticUnitId,
    IReadOnlyList<ModbusRegisterMapping> Registers,
    // ADR 0013 §5.1: mapping-file schema version. Trailing default keeps
    // existing in-memory construction sites unbroken; the loader sets it
    // from the file. Enforcement (required + pre-check) lands in step 1b.
    string SchemaVersion = "v1");
