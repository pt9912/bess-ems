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

    // Replaces the schedule for (assetId, type) with optimistic
    // concurrency control (RM-M3-FUP-02). M1 keeps only the most
    // recent version per (asset, type); historical retention is
    // RM-M1-14.
    //
    // Concurrency contract: `expectedBaseVersion` is the version the
    // caller saw when it last read the schedule. `0` means "no prior
    // version exists" (insert path); `> 0` means "expect exactly this
    // version as the base" (CAS update path). A mismatch — including
    // an existing row when 0 was passed, or a stale version when N>0
    // was passed — throws ScheduleConcurrencyConflictException
    // instead of silently overwriting. Callers must pass `>= 0`;
    // negative values are an ArgumentOutOfRangeException at the
    // boundary.
    void Replace(Schedule schedule, int expectedBaseVersion);
}
