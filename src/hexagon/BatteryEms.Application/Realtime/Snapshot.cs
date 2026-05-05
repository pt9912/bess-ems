using BatteryEms.Domain;

namespace BatteryEms.Application.Realtime;

public sealed record Snapshot(
    BatteryTelemetry Telemetry,
    DateTimeOffset ReceivedAt,
    DataQuality Quality);
