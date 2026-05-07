using BatteryEms.Application.Optimization;
using BatteryEms.Domain;

namespace BatteryEms.Application.Api;

// LH-API-005 driving port for the schedule-optimisation pipeline. The
// HTTP layer (RM-M2-OP-07) binds POST /markets/day-ahead/optimize to
// ExecuteAsync; the use case orchestrates the IScheduleOptimizer
// driven port, the OptimizationRun persistence and the Schedule
// repository so the API never sees those collaborators directly.
public interface IScheduleOptimizationUseCase
{
    Task<ScheduleOptimizationOutcome> ExecuteAsync(
        ScheduleOptimizationInputs inputs,
        CancellationToken cancellationToken);
}

// API-facing summary of one optimisation attempt. The full LH-OPT-009
// payload sits on the persisted OptimizationRun; the outcome only
// surfaces the fields a caller needs immediately after triggering a
// run: which run was created, what the solver said, whether a new
// schedule version was produced and the human-readable termination
// reason.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record ScheduleOptimizationOutcome(
    Guid RunId,
    OptimizationSolverStatus Status,
    int? ProducedScheduleVersion,
    string TerminationReason);
