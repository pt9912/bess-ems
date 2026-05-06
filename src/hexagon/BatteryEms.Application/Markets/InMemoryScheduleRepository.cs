using System.Collections.Concurrent;
using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

public sealed class InMemoryScheduleRepository : IScheduleRepository
{
    private readonly ConcurrentDictionary<(string AssetId, ScheduleType Type), Schedule> _byKey = new();

    public InMemoryScheduleRepository(IEnumerable<Schedule>? seed = null)
    {
        if (seed is null)
        {
            return;
        }

        foreach (var schedule in seed)
        {
            _byKey[(schedule.AssetId, schedule.Type)] = schedule;
        }
    }

    public IEnumerable<Schedule> FindAll(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return _byKey
            .Where(kv => string.Equals(kv.Key.AssetId, assetId, StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .ToArray();
    }

    public Schedule? FindActive(string assetId, ScheduleType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return _byKey.TryGetValue((assetId, type), out var schedule) ? schedule : null;
    }

    public void Replace(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        _byKey[(schedule.AssetId, schedule.Type)] = schedule;
    }
}
