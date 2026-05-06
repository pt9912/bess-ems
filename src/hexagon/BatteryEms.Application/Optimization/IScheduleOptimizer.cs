namespace BatteryEms.Application.Optimization;

// Driven port for horizon-level optimisation (LH-OPT-001 / LH-OPT-007).
//
// IScheduleOptimizer is **not** the regulation-cycle dispatcher — that
// role is owned by IDispatchOptimizer in the same namespace. The split
// is structural:
//
//   IDispatchOptimizer  : 1-Hz single-step setpoint inside the
//                         ControlCycleUseCase; latency-critical;
//                         must complete within the regulation tick.
//   IScheduleOptimizer  : full horizon (typically 24 h with 1-h step);
//                         off-cycle solver run that emits a versioned
//                         Domain.Schedule consumed via IScheduleTracker;
//                         expected to take seconds to minutes depending
//                         on the solver backend.
//
// LH-OPT-007 ("Trennung von Optimierung und Regelung") forbids the two
// from collapsing into one pipeline. The architecture-tabu test
// HorizonAndDispatchAreSeparate enforces it at the type level: the
// schedule optimiser must not depend on dispatch-cycle types and the
// dispatcher must not depend on horizon-level types.
public interface IScheduleOptimizer
{
    Task<ScheduleOptimizationResult> OptimizeAsync(
        ScheduleOptimizationRequest request,
        CancellationToken cancellationToken);
}
