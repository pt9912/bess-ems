using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// In-memory dedupe store for tests + the M4-03 happy-path Application
// wiring (plan-RM-M4-03 §145). Per-source nested map; retention
// compaction on each accept keeps:
//   - the most recent entry per source unconditionally, and
//   - all entries within the replay window
//     max(MaxAge + FutureSkewTolerance + DedupeWindow, 60s) by
//     winner_chosen_at,
//   - capped at RegelleistungOptions.MaxEntriesPerSource.
//
// MarkInvalid()/Recover() are explicit test affordances: the
// production InMemory variant never enters Invalid on its own (it has
// no checkpoint to load), but tests for Sub-Slices C/D need a way to
// surface the RejectedDedupeStoreInvalid path through the same port.
// The Dapper variant detects the four real fail-closed sub-cases
// (incompatible checkpoint, oversize per-source, partial corruption,
// parse/decode fail) at load time.
public sealed class InMemoryActivationDedupeStore : IActivationDedupeStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, DedupeEntry>> _bySource = new();
    private readonly RegelleistungOptions _options;
    private readonly IClock _clock;
    private bool _invalid;

    public InMemoryActivationDedupeStore(RegelleistungOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        options.EnsureValid();
        _options = options;
        _clock = clock;
    }

    public Task<AcceptResult> TryAcceptAsync(
        RegelleistungActivation activation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_invalid)
            {
                return Task.FromResult(AcceptResult.RejectedDedupeStoreInvalid);
            }

            if (!_bySource.TryGetValue(activation.SourceId, out var perSource))
            {
                perSource = new Dictionary<string, DedupeEntry>(StringComparer.Ordinal);
                _bySource[activation.SourceId] = perSource;
            }

            if (perSource.TryGetValue(activation.ActivationId, out var existing))
            {
                // Plan §145: gleicher Hash = ReplayIdempotent, anderer Hash = DedupeConflict.
                return Task.FromResult(string.Equals(existing.PayloadHash, activation.PayloadHash, StringComparison.Ordinal)
                    ? AcceptResult.ReplayIdempotent
                    : AcceptResult.RejectedDedupeConflict);
            }

            var now = _clock.UtcNow;
            perSource[activation.ActivationId] = new DedupeEntry(
                activation.SequenceNumber,
                activation.SignalTimestampUtc,
                activation.PayloadHash,
                WinnerChosenAt: now);
            CompactSource(perSource, now);
            return Task.FromResult(AcceptResult.Accepted);
        }
    }

    // Test affordance: simulate the dedupe-store-invalid path so
    // higher-level tests (Sub-Slice C/D) can pin the gate behaviour
    // without a real Postgres fixture.
    public void MarkInvalid()
    {
        lock (_gate)
        {
            _invalid = true;
        }
    }

    public void Recover()
    {
        lock (_gate)
        {
            _invalid = false;
        }
    }

    public int CountForSource(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        lock (_gate)
        {
            return _bySource.TryGetValue(sourceId, out var perSource) ? perSource.Count : 0;
        }
    }

    public bool IsInvalid
    {
        get { lock (_gate) { return _invalid; } }
    }

    private void CompactSource(Dictionary<string, DedupeEntry> perSource, DateTimeOffset now)
    {
        // Replay window: signals older than this can no longer pass the
        // time-validator's MaxAge check, so the dedupe entry is no
        // longer reachable via fresh receptions. The 60s floor matches
        // plan §145 and shields against tiny operator-tuned tolerances.
        var window = _options.MaxAge + _options.FutureSkewTolerance + _options.DedupeWindow;
        if (window < TimeSpan.FromSeconds(60))
        {
            window = TimeSpan.FromSeconds(60);
        }
        var cutoff = now - window;

        // Pass 1: drop entries strictly older than cutoff, but never
        // drop the single most recent — replay detection across long
        // quiet periods relies on the "letzter Checkpoint" guarantee.
        if (perSource.Count > 1)
        {
            var newest = FindNewest(perSource);
            var stale = new List<string>();
            foreach (var kv in perSource)
            {
                if (!string.Equals(kv.Key, newest.Key, StringComparison.Ordinal)
                    && kv.Value.WinnerChosenAt < cutoff)
                {
                    stale.Add(kv.Key);
                }
            }
            foreach (var key in stale)
            {
                perSource.Remove(key);
            }
        }

        // Pass 2: hard cap. If we still have more than MaxEntriesPerSource,
        // evict the oldest by winner_chosen_at first. Always preserve the
        // single most recent.
        while (perSource.Count > _options.MaxEntriesPerSource)
        {
            var oldest = FindOldest(perSource);
            perSource.Remove(oldest.Key);
        }
    }

    private static KeyValuePair<string, DedupeEntry> FindNewest(
        Dictionary<string, DedupeEntry> perSource)
    {
        KeyValuePair<string, DedupeEntry>? newest = null;
        foreach (var kv in perSource)
        {
            if (newest is null || kv.Value.WinnerChosenAt > newest.Value.Value.WinnerChosenAt)
            {
                newest = kv;
            }
        }
        return newest!.Value;
    }

    private static KeyValuePair<string, DedupeEntry> FindOldest(
        Dictionary<string, DedupeEntry> perSource)
    {
        KeyValuePair<string, DedupeEntry>? oldest = null;
        foreach (var kv in perSource)
        {
            if (oldest is null || kv.Value.WinnerChosenAt < oldest.Value.Value.WinnerChosenAt)
            {
                oldest = kv;
            }
        }
        return oldest!.Value;
    }

    private sealed record DedupeEntry(
        long SequenceNumber,
        DateTimeOffset SignalTimestampUtc,
        string PayloadHash,
        DateTimeOffset WinnerChosenAt);
}
