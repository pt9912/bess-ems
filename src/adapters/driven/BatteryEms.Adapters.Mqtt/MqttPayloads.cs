using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryEms.Adapters.Mqtt;

// Wire DTOs for the EMS↔simulator MQTT contract. Field naming uses
// snake_case so the same JSON works against the Go simulator
// (simulators/bess-field-sim/internal/model/{telemetry,command}.go),
// and Mode/Source travel as enum names ("Stop"|"Charge"|"Discharge"|
// "Idle" / "Schedule"|"Operator"|"RegelLeistung"|"Safety"|
// "Optimization"|"Fallback") so both sides agree on a single string
// vocabulary instead of mapping integers across the language boundary.

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer.Deserialize via reflection.")]
internal sealed record TelemetrySnapshotPayload(
    [property: JsonPropertyName("offset_millis")] long OffsetMillis,
    [property: JsonPropertyName("soc_percent")] double SocPercent,
    [property: JsonPropertyName("soh_percent")] double SohPercent,
    [property: JsonPropertyName("active_power_kw")] double ActivePowerKw,
    [property: JsonPropertyName("reactive_power_kvar")] double ReactivePowerKvar,
    [property: JsonPropertyName("dc_voltage")] double DcVoltage,
    [property: JsonPropertyName("dc_current")] double DcCurrent,
    [property: JsonPropertyName("temperature_celsius")] double TemperatureCelsius,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("fault_status")] string FaultStatus);

internal sealed record CommandPayload(
    [property: JsonPropertyName("command_id")] string CommandId,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("asset_id")] string AssetId,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("active_power_kw")] double ActivePowerKw,
    [property: JsonPropertyName("reactive_power_kvar")] double? ReactivePowerKvar,
    [property: JsonPropertyName("valid_until")] DateTimeOffset ValidUntil,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("source")] string Source);

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by JsonSerializer.Deserialize via reflection.")]
internal sealed record CommandAckPayload(
    [property: JsonPropertyName("command_id")] string CommandId,
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("dispatched_at")] DateTimeOffset DispatchedAt,
    [property: JsonPropertyName("reason")] string? Reason);

internal static class MqttJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
