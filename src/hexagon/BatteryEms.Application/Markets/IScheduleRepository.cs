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
    void Replace(Schedule schedule);
}
