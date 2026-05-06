namespace BatteryEms.Domain;

// Solver-agnostic status taxonomy for an OptimizationRun (LH-OPT-009).
// Maps onto every solver M2 can plausibly host (HiGHS, OR-Tools,
// heuristics, gRPC sidecar): the four "the solver finished" states
// (Optimal/Feasible/Infeasible/Unbounded), the resource-limit cases
// (TimeLimit/IterationLimit), and the catch-all Failed for solver
// crashes or driver-level errors.
public enum OptimizationSolverStatus
{
    // Solver returned a provably optimal solution within tolerance.
    Optimal,

    // Solver returned a feasible solution but could not prove optimality
    // (typically because a time or gap limit kicked in first).
    Feasible,

    // Solver proved the model has no feasible solution.
    Infeasible,

    // Solver detected the objective is unbounded along a feasible ray.
    Unbounded,

    // Solver hit the configured wall-clock budget.
    TimeLimit,

    // Solver hit the configured iteration / node budget.
    IterationLimit,

    // Solver crashed, bindings raised, or the solver returned an
    // unmappable status. TerminationReason on OptimizationRun carries
    // the human-readable detail.
    Failed,
}
