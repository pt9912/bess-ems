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
    int? SunspecModel)
{
    // LH-DOM-005 device-point metadata: optional, init-only so existing
    // call sites that build the record positionally stay source-compatible.
    public DevicePointMetadata? DevicePoint { get; init; }
}
