namespace BatteryEms.Application.Configuration;

// Cross-protocol device-point metadata (LH-DOM-005). Modbus registers
// and MQTT topics carry an optional instance of this record so
// telemetry, API, monitoring and future export adapters share the same
// fachliche Bedeutung. Protocol-specific fields stay on the protocol-
// specific mapping (Modbus address/type/scale, MQTT topic/direction).
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record DevicePointMetadata(
    string? DisplayName,
    string? Unit,
    bool Exportable,
    DevicePointAlarm? Alarm,
    IReadOnlyDictionary<string, string>? ValueExplanation);

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record DevicePointAlarm(
    double? Min,
    double? Max,
    string? Severity);
