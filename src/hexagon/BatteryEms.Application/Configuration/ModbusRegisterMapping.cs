namespace BatteryEms.Application.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record ModbusRegisterMapping(
    string Name,
    int Address,
    string Type,
    double ScaleFactor,
    double RangeMin,
    double RangeMax,
    bool Writable,
    string WriteCadence,
    string AuthRequired,
    IReadOnlyDictionary<int, string>? Enum,
    string? FirmwareConstraint,
    int? SunspecModel);
