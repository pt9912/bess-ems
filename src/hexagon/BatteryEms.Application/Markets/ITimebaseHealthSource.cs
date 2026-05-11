using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// Driven port (read side) carrying the current TimebaseDebounceState
// across the process boundary between the cycle owner (which observes
// timebase stability cycle-by-cycle) and the consumers (activation
// pipeline, production-precondition provider, health endpoint) that
// gate every reception against the current state.
public interface ITimebaseHealthSource
{
    TimebaseDebounceState Current { get; }
}

// Driven port (write side). The control-cycle owner injects this
// interface and calls Observe() per tick. CQRS split keeps reader
// contracts (`ITimebaseHealthSource`) free of mutation methods so
// consumer adapters cannot accidentally drive the state machine.
//
// Plan-RM-M4-03 §144 puts the debounce primitive "lebt im
// ControlCycle-Use-Case"; the cycle is the writer that calls Observe()
// per tick from `ControlCycleHostedService` (see Worker layer).
public interface ITimebaseHealthObserver
{
    // Each tick reports a violation/stable observation; the observer
    // advances the debounce state machine (3-in-10 → Degraded,
    // 5-stable → Healthy).
    void Observe(bool violationThisCycle);

    // Operator-explicit recovery hook: clears the Degraded state back
    // to a Healthy initial regardless of the violation history.
    void Recover();
}

public sealed class InMemoryTimebaseHealthSource
    : ITimebaseHealthSource, ITimebaseHealthObserver
{
    private readonly object _gate = new();
    private TimebaseDebounceState _state = TimebaseDebounceState.Initial;

    public TimebaseDebounceState Current
    {
        get { lock (_gate) { return _state; } }
    }

    public void Observe(bool violationThisCycle)
    {
        lock (_gate)
        {
            _state = _state.Observe(violationThisCycle);
        }
    }

    public void Recover()
    {
        lock (_gate)
        {
            _state = _state.Recover();
        }
    }
}
