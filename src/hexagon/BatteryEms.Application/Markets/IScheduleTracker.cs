using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// Driving-side use case that turns the repository's persisted schedules
// into the MarketCommitment list the dispatch uses each cycle. Lives in
// Application (not Domain) because it queries a port; the per-window
// math itself stays inside Schedule.WindowCovering.
public interface IScheduleTracker
{
    IReadOnlyList<MarketCommitment> GetActiveCommitments(string assetId, DateTimeOffset moment);
}
