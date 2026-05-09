using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// Driven port for held reserve capacity (LH-MKT-004). The schedule
// optimiser asks for the bands overlapping a given asset's planning
// horizon and deducts them from the asset's available capacity per
// step. RM-M4-02 ships the in-memory adapter; a Dapper-backed
// implementation lands once a real persistence consumer arrives
// (typically together with operator tooling for reserve-band
// management — separate slice).
public interface IReserveRepository
{
    // All reserve bands for the asset that overlap the half-open
    // [horizonStart, horizonEnd) interval. Caller-side ordering is not
    // guaranteed; the optimiser sums same-direction bands per step so
    // overlapping FCR + AFRR-Up entries compose without surprises.
    IReadOnlyList<ReserveBand> FindActive(
        string assetId,
        DateTimeOffset horizonStart,
        DateTimeOffset horizonEnd);
}
