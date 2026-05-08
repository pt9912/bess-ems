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

    // RM-M2-HIL-01: register table for reads. Default 'holding' keeps
    // M1 profiles untouched; 'input' is consumed by the HIL-02 adapter
    // change.
    public string RegisterTable { get; init; } = ModbusRegisterTables.Holding;

    // RM-M2-HIL-01: 32-bit word order. Default 'high_low' matches the
    // existing RegisterDecoder; 'low_high' lands with HIL-03.
    public string WordOrder { get; init; } = ModbusWordOrders.HighLow;
}
