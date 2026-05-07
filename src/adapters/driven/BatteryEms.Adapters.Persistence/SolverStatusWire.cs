using BatteryEms.Domain;

namespace BatteryEms.Adapters.Persistence;

// Wire format for OptimizationSolverStatus. Stable, snake_case strings —
// the API renders the same values via the snake_case enum converter
// (RM-M2-OP-07), so the wire matches what operators see in the run
// history and what humans typed when triaging old runs.
internal static class SolverStatusWire
{
    public static string ToWire(OptimizationSolverStatus status) => status switch
    {
        OptimizationSolverStatus.Optimal => "optimal",
        OptimizationSolverStatus.Feasible => "feasible",
        OptimizationSolverStatus.Infeasible => "infeasible",
        OptimizationSolverStatus.Unbounded => "unbounded",
        OptimizationSolverStatus.TimeLimit => "time_limit",
        OptimizationSolverStatus.IterationLimit => "iteration_limit",
        OptimizationSolverStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown solver status."),
    };

    public static OptimizationSolverStatus FromWire(string wire) => wire switch
    {
        "optimal" => OptimizationSolverStatus.Optimal,
        "feasible" => OptimizationSolverStatus.Feasible,
        "infeasible" => OptimizationSolverStatus.Infeasible,
        "unbounded" => OptimizationSolverStatus.Unbounded,
        "time_limit" => OptimizationSolverStatus.TimeLimit,
        "iteration_limit" => OptimizationSolverStatus.IterationLimit,
        "failed" => OptimizationSolverStatus.Failed,
        _ => throw new InvalidOperationException($"Unknown solver status '{wire}' in storage."),
    };
}
