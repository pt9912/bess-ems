using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Application.Optimization;

// Placeholder schedule optimiser used until RM-M2-OP-05 plugs in the
// real LP solver (OR-Tools per RM-M2-OP-OPEN-01). Always emits a Failed
// run with the human-readable reason "no-solver-configured" so the API
// surface (RM-M2-OP-07) and the use case (RM-M2-OP-03) can be exercised
// end-to-end before the solver lands.
//
// The result still satisfies every ScheduleOptimizationResult invariant
// (Failed status carries no produced schedule, run reference is null),
// so the rest of the pipeline behaves identically to the eventual
// production solver when it cannot find a solution.
public sealed class NoOpScheduleOptimizer : IScheduleOptimizer
{
    private const string SolverName = "noop-schedule-optimizer";
    private const string TerminationCode = "no-solver-configured";

    private readonly IClock _clock;

    public NoOpScheduleOptimizer(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public Task<ScheduleOptimizationResult> OptimizeAsync(
        ScheduleOptimizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var run = new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: request.AssetId,
            solverName: SolverName,
            status: OptimizationSolverStatus.Failed,
            horizonStart: request.HorizonStart,
            horizonEnd: request.HorizonEnd,
            timeStep: request.TimeStep,
            objectiveValue: 0,
            objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            solverRuntime: TimeSpan.Zero,
            terminationCode: TerminationCode,
            terminationDetail: null,
            createdAt: _clock.UtcNow,
            inputs: request.Inputs,
            producedSchedule: null);
        return Task.FromResult(new ScheduleOptimizationResult(run, producedSchedule: null));
    }
}
