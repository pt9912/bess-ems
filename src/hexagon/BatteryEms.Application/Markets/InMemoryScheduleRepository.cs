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

    public void Replace(Schedule schedule, int expectedBaseVersion)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (expectedBaseVersion < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedBaseVersion),
                "expectedBaseVersion must be >= 0 (0 = no prior version).");
        }

        var key = (schedule.AssetId, schedule.Type);
        // AddOrUpdate is the only ConcurrentDictionary primitive that
        // serialises read+write under a per-key lock — exactly what
        // CAS needs. The updateValueFactory throws on mismatch; the
        // dictionary returns the new value on success.
        _byKey.AddOrUpdate(
            key,
            addValueFactory: _ =>
            {
                if (expectedBaseVersion != 0)
                {
                    throw new ScheduleConcurrencyConflictException(
                        schedule.AssetId, schedule.Type, expectedBaseVersion, actualVersion: 0);
                }
                return schedule;
            },
            updateValueFactory: (_, existing) =>
            {
                if (existing.Version != expectedBaseVersion)
                {
                    throw new ScheduleConcurrencyConflictException(
                        schedule.AssetId, schedule.Type, expectedBaseVersion, existing.Version);
                }
                return schedule;
            });
    }
}
