using BatteryEms.Domain;

namespace BatteryEms.Application.Persistence;

// LH-PERSIST-001 — every received telemetry tick is append-only; queries
// support the API's historical read paths (RM-M1-15) and any later
// debugging or compliance question. Implementations must persist the
// full DataQuality state alongside the values so a reader can tell
// whether a sample was usable for control.
public interface ITelemetryRepository
{
    Task AppendAsync(BatteryTelemetry telemetry, CancellationToken cancellationToken);

    // Half-open range [from, until) by convention — same time-window
    // shape as Schedule.WindowCovering so callers don't have to reason
    // about two interval semantics.
    Task<IReadOnlyList<BatteryTelemetry>> QueryAsync(
        string assetId,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken);

    Task<BatteryTelemetry?> FindLatestAsync(string assetId, CancellationToken cancellationToken);
}
