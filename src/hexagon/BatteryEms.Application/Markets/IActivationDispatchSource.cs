using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// Driven port that carries an accepted Regelleistung activation
// across to the dispatch optimizer (plan-RM-M4-03 §147, D-09 choice c).
// The use-case calls Submit after a Successful + dispatch-relevant
// outcome; ScheduleFollowingDispatchOptimizer calls GetActive per
// tick and treats a non-null return as a rank-3 winner over all
// MarketCommitments. DispatchRequest format stays unchanged — the
// activation reaches the optimizer through this port, not through a
// new field on the request record.
//
// For Sub-Slice D the source holds at most a single activation; new
// submissions replace the prior one when a tiebreak ranks them ahead
// (higher SequenceNumber, then newer SignalTimestampUtc, then
// lex-smaller (source_id, activation_id) per plan §148). Multi-source
// concurrent contention with degenerate tiebreak — the
// AmbiguousDuplicate path — is Sub-Slice E scope.
public interface IActivationDispatchSource
{
    void Submit(RegelleistungActivation activation);
    RegelleistungActivation? GetActive(DateTimeOffset now);
    void Clear();
}

public sealed class InMemoryActivationDispatchSource : IActivationDispatchSource
{
    private readonly object _gate = new();
    private RegelleistungActivation? _held;

    public void Submit(RegelleistungActivation activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        lock (_gate)
        {
            if (_held is null || ShouldReplace(_held, activation))
            {
                _held = activation;
            }
        }
    }

    public RegelleistungActivation? GetActive(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_held is null)
            {
                return null;
            }
            // Plan §147: the Optimizer verwirft abgelaufene Kandidaten
            // aktiv pro Tick. ValidUntil is exclusive (half-open), so a
            // tick exactly at ValidUntil already returns null.
            return _held.CoversValidity(now) ? _held : null;
        }
    }

    public void Clear()
    {
        lock (_gate) { _held = null; }
    }

    // Tiebreak per plan §148: higher SequenceNumber wins; then newer
    // SignalTimestampUtc; then lex-smallest (source_id, activation_id).
    // Returns true when candidate strictly beats existing — equal
    // ranks leave the existing held activation in place (single-slot
    // semantics; AmbiguousDuplicate handling is Sub-Slice E scope).
    private static bool ShouldReplace(
        RegelleistungActivation existing,
        RegelleistungActivation candidate)
    {
        if (candidate.SequenceNumber != existing.SequenceNumber)
        {
            return candidate.SequenceNumber > existing.SequenceNumber;
        }
        if (candidate.SignalTimestampUtc != existing.SignalTimestampUtc)
        {
            return candidate.SignalTimestampUtc > existing.SignalTimestampUtc;
        }
        var sourceCmp = string.CompareOrdinal(candidate.SourceId, existing.SourceId);
        if (sourceCmp != 0)
        {
            return sourceCmp < 0;
        }
        return string.CompareOrdinal(candidate.ActivationId, existing.ActivationId) < 0;
    }
}

// Test stub matching the NoOp pattern from D-09: every M2 dispatch
// test that constructs ScheduleFollowingDispatchOptimizer threads a
// NoOp source through so the activation pipeline stays inert and the
// test pins schedule-following behaviour. Production wiring uses
// InMemoryActivationDispatchSource.
public sealed class NoOpActivationDispatchSource : IActivationDispatchSource
{
    public void Submit(RegelleistungActivation activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        // Intentional no-op: this stub never holds an activation.
    }

    public RegelleistungActivation? GetActive(DateTimeOffset now) => null;

    public void Clear() { }
}
