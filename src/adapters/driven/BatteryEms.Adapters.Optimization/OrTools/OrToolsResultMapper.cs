using BatteryEms.Domain;
using Google.OrTools.LinearSolver;

namespace BatteryEms.Adapters.Optimization.OrTools;

// Maps OR-Tools' Solver.ResultStatus into the backend-neutral
// OptimizationSolverStatus the rest of the system speaks. Kept as a
// separate file so a future second backend (HiGHS / CBC) can plug a
// sibling mapper alongside without touching the optimizer body.
//
// MODEL_INVALID, ABNORMAL and NOT_SOLVED collapse onto Failed because
// LH-OPT-009 treats every "couldn't produce a usable answer" path as a
// single audit class — TerminationReason carries the human-readable
// distinction.
internal static class OrToolsResultMapper
{
    public static (OptimizationSolverStatus Status, string Code, string? Detail) Map(
        Solver.ResultStatus backendStatus,
        TimeSpan elapsed,
        TimeSpan? timeLimit)
    {
        // GLOP surfaces NOT_SOLVED both for "I gave up" and for "I never
        // started"; if a TimeLimit was set and we *strictly* crossed it,
        // map NOT_SOLVED to the dedicated TimeLimit status so the status
        // counter doesn't lump deadline misses with crashes. Strict `>`
        // (review #2) protects a FEASIBLE solve that finishes exactly at
        // the budget boundary from being re-classified — its schedule
        // would otherwise be discarded by the caller.
        if (timeLimit is { } limit
            && elapsed > limit
            && backendStatus == Solver.ResultStatus.NOT_SOLVED)
        {
            return (OptimizationSolverStatus.TimeLimit,
                "or-tools-time-limit",
                $"{elapsed.TotalSeconds:F3}s > {limit.TotalSeconds:F3}s");
        }

        return backendStatus switch
        {
            Solver.ResultStatus.OPTIMAL => (OptimizationSolverStatus.Optimal, "or-tools-optimal", null),
            Solver.ResultStatus.FEASIBLE => (OptimizationSolverStatus.Feasible, "or-tools-feasible-not-proven-optimal", null),
            Solver.ResultStatus.INFEASIBLE => (OptimizationSolverStatus.Infeasible, "or-tools-infeasible", null),
            Solver.ResultStatus.UNBOUNDED => (OptimizationSolverStatus.Unbounded, "or-tools-unbounded", null),
            Solver.ResultStatus.ABNORMAL => (OptimizationSolverStatus.Failed, "or-tools-abnormal", null),
            Solver.ResultStatus.MODEL_INVALID => (OptimizationSolverStatus.Failed, "or-tools-model-invalid", null),
            Solver.ResultStatus.NOT_SOLVED => (OptimizationSolverStatus.Failed, "or-tools-not-solved", null),
            // Unreachable under the OR-Tools 9.x ResultStatus contract;
            // kept as defensive fallback so a future SDK enum addition
            // doesn't silently drop into the wrong branch.
            _ => UnknownStatus(backendStatus),
        };
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static (OptimizationSolverStatus, string, string?) UnknownStatus(Solver.ResultStatus status) =>
        (OptimizationSolverStatus.Failed, "or-tools-unknown-status", ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
}
