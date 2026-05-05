namespace BatteryEms.Application.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record ModbusMappingConfiguration(
    string ProfileName,
    string UnitIdDiscovery,
    int? StaticUnitId,
    IReadOnlyList<ModbusRegisterMapping> Registers);
