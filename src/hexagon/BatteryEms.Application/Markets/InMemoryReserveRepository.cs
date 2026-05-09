using System.Collections.Concurrent;
using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// M4-default reserve store. Today's *production wiring* is
// seeded-at-startup only: the host registers an empty repository, no
// production caller writes through `Add`. The public `Add` method
// exists for tests and for future write paths but is currently
// unused outside test code. Once a production write path arrives
// (operator API, hourly roll-forward seeder, etc.) the unbounded
// ConcurrentBag becomes a leak; switch to a TTL/LRU structure at
// that point. RM-M4-02 deliberately leaves eviction out.
public sealed class InMemoryReserveRepository : IReserveRepository
{
    private readonly ConcurrentBag<ReserveBand> _bands = new();

    public InMemoryReserveRepository(IEnumerable<ReserveBand>? seed = null)
    {
        if (seed is null)
        {
            return;
        }
        foreach (var band in seed)
        {
            _bands.Add(band);
        }
    }

    public void Add(ReserveBand band)
    {
        ArgumentNullException.ThrowIfNull(band);
        _bands.Add(band);
    }

    public IReadOnlyList<ReserveBand> FindActive(
        string assetId,
        DateTimeOffset horizonStart,
        DateTimeOffset horizonEnd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (horizonStart >= horizonEnd)
        {
            throw new ArgumentException(
                $"horizonStart must be before horizonEnd ({horizonStart:O} -> {horizonEnd:O}).",
                nameof(horizonStart));
        }
        // Half-open overlap: a band participates if its window
        // [Start, End) intersects [horizonStart, horizonEnd) — i.e.
        // band.Start < horizonEnd AND band.End > horizonStart.
        return _bands
            .Where(b =>
                string.Equals(b.AssetId, assetId, StringComparison.Ordinal) &&
                b.Start < horizonEnd &&
                b.End > horizonStart)
            .ToArray();
    }
}
