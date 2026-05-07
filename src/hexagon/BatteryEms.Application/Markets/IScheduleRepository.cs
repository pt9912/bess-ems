using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// Driven port for schedule persistence. M1 ships an in-memory adapter
// (InMemoryScheduleRepository); RM-M1-13 will plug a PostgreSQL-backed
// implementation behind the same interface.
public interface IScheduleRepository
{
    // All schedules currently active for the asset, one per ScheduleType
    // that has been Replaced. Caller-side ordering is not guaranteed.
    IEnumerable<Schedule> FindAll(string assetId);

    // The latest schedule replaced for (assetId, type), or null if none.
    Schedule? FindActive(string assetId, ScheduleType type);

    // Replaces the schedule for (assetId, type). M1 keeps only the most
    // recent version per (asset, type); historical retention is RM-M1-14.
    //
    // Concurrency contract (M2): Replace is *unconditional* — it has no
    // expected-version check, so two callers reading the same Version=v3
    // and writing v4 will both succeed and the second write silently
    // overwrites the first. M2 relies on caller-side serialisation
    // (DefaultScheduleOptimizationUseCase per-(asset, type) SemaphoreSlim,
    // single host process). A multi-replica deployment requires
    // RM-M2-OP-OPEN-05 to land first: this signature gains an
    // `expectedBaseVersion` parameter and the Dapper implementation
    // adds `WHERE version = @expected` so a stale write fails fast
    // instead of clobbering the live schedule.
    void Replace(Schedule schedule);
}
