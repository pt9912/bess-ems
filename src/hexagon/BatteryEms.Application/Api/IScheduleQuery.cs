using BatteryEms.Domain;

namespace BatteryEms.Application.Api;

// LH-API-004 driving port. M1 surfaces only the currently-active schedule
// per (asset, type); historical / versioned reads are post-MVP and will
// extend the interface without breaking the M1 shape. Returning an empty
// list is the explicit "asset known but no schedule loaded yet" case.
public interface IScheduleQuery
{
    IReadOnlyList<Schedule> FindCurrent(string assetId);
}
