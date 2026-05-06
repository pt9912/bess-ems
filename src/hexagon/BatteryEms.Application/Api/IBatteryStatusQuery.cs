using BatteryEms.Domain;

namespace BatteryEms.Application.Api;

// LH-API-002 + LH-API-003 driving port: the API surfaces both the
// realtime snapshot (status) and the most recent command (current
// command) for one asset. Returning null = asset unknown / no
// observation yet, which the API layer maps to HTTP 404.
public interface IBatteryStatusQuery
{
    Task<BatteryStatusView?> FindAsync(string assetId, DateTimeOffset now, CancellationToken cancellationToken);
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record BatteryStatusView(
    string AssetId,
    BatteryTelemetry? Telemetry,
    DataQuality? Quality,
    DateTimeOffset? ObservedAt,
    BatteryCommand? LastCommand);
