using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// Driven port carrying the current TimebaseDebounceState across the
// process boundary between the cycle owner (which observes timebase
// stability cycle-by-cycle) and the activation use-case (which reads
// the current state to gate every reception).
//
// Plan-RM-M4-03 §144 puts the debounce primitive "lebt im
// ControlCycle-Use-Case" — the cycle is the writer that calls
// Observe() per tick. Sub-Slice D introduces this port so the
// activation pipeline can read the state without being coupled to the
// cycle's lifecycle. Default impl is in-memory; the cycle wiring lands
// when a real timebase observation source comes online.
public interface ITimebaseHealthSource
{
    TimebaseDebounceState Current { get; }
}

public sealed class InMemoryTimebaseHealthSource : ITimebaseHealthSource
{
    private readonly object _gate = new();
    private TimebaseDebounceState _state = TimebaseDebounceState.Initial;

    public TimebaseDebounceState Current
    {
        get { lock (_gate) { return _state; } }
    }

    // Cycle-owner update path. Each tick reports a violation/stable
    // observation; the source advances the debounce state machine.
    public void Observe(bool violationThisCycle)
    {
        lock (_gate)
        {
            _state = _state.Observe(violationThisCycle);
        }
    }

    // Operator-explicit recovery hook: clears the Degraded state back
    // to a Healthy initial regardless of the violation history.
    public void Recover()
    {
        lock (_gate)
        {
            _state = _state.Recover();
        }
    }
}
