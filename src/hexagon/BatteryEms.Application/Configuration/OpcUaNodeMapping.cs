namespace BatteryEms.Application.Configuration;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record OpcUaNodeMapping(
    string Name,
    string NodeId,
    string Direction,
    string DataType,
    double ScaleFactor,
    bool Writable,
    string AuthRequired,
    string? WriteCadence = null,
    int? MonitoringIntervalMs = null)
{
    // LH-DOM-005 device-point metadata (analog to Modbus and MQTT
    // mappings): optional, init-only so existing call sites that build
    // the record positionally stay source-compatible when M4-04 starts
    // wiring real OPC-UA reads/writes against this mapping.
    public DevicePointMetadata? DevicePoint { get; init; }
}
