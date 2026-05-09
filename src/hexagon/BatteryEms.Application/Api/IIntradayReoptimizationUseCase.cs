namespace BatteryEms.Application.Api;

// RM-M4-01 driving port for residual-horizon Intraday reoptimisation.
// The HTTP layer binds POST /markets/intraday/reoptimize to
// ExecuteAsync; the use case orchestrates the existing Intraday
// schedule lookup, the residual-horizon LP via IScheduleOptimizer,
// the OptimizationRun persistence and the Schedule-Replace under
// the same per-(asset, type) lock + CAS path the day-ahead pipeline
// uses (RM-M3-FUP-02).
//
// The outcome shape is reused from ScheduleOptimizationOutcome —
// the API contract (RunId, Status, ProducedScheduleVersion,
// TerminationReason) is identical.
public interface IIntradayReoptimizationUseCase
{
    Task<ScheduleOptimizationOutcome> ExecuteAsync(
        IntradayReoptimizationCommand command,
        CancellationToken cancellationToken);
}
