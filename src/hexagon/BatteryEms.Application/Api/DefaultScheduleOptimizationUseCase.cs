using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Persistence;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Application.Api;

// Wires the schedule-optimisation pipeline together: ask the driven
// IScheduleOptimizer for a result, append the OptimizationRun to the
// run repository (LH-PERSIST-007), and — when the solver produced a
// usable schedule — replace the asset's schedule of that type so the
// downstream IScheduleTracker / IDispatchOptimizer pair picks up the
// new version on the next regulation cycle.
//
// Errors raised by the optimiser bubble out: appending a run record
// that wasn't actually produced would lie to the audit log. The API
// layer turns the bubble into the appropriate HTTP shape (RM-M2-OP-07).
public sealed partial class DefaultScheduleOptimizationUseCase : IScheduleOptimizationUseCase
{
    private readonly IScheduleOptimizer _optimizer;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IOptimizationRunRepository _runRepository;
    private readonly IOptimizationRunMetrics _metrics;
    private readonly ILogger<DefaultScheduleOptimizationUseCase> _logger;

    public DefaultScheduleOptimizationUseCase(
        IScheduleOptimizer optimizer,
        IScheduleRepository scheduleRepository,
        IOptimizationRunRepository runRepository,
        IOptimizationRunMetrics metrics,
        ILogger<DefaultScheduleOptimizationUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(optimizer);
        ArgumentNullException.ThrowIfNull(scheduleRepository);
        ArgumentNullException.ThrowIfNull(runRepository);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        _optimizer = optimizer;
        _scheduleRepository = scheduleRepository;
        _runRepository = runRepository;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ScheduleOptimizationOutcome> ExecuteAsync(
        ScheduleOptimizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _optimizer.OptimizeAsync(request, cancellationToken).ConfigureAwait(false);

        // The result invariants (Run.HasUsableSolution ⇔ ProducedSchedule
        // non-null, version + reference matching) are already checked in
        // ScheduleOptimizationResult's constructor; we can rely on them.
        await _runRepository.AppendAsync(result.Run, cancellationToken).ConfigureAwait(false);

        // Metrics fire after the run is durably persisted so a /metrics
        // scrape and the persisted run history can never disagree on
        // counts (LH-OPT-009 audit-stance: a run that wasn't appended
        // didn't happen, so it shouldn't be counted either).
        _metrics.Record(result.Run);

        int? producedVersion = null;
        if (result.ProducedSchedule is not null)
        {
            _scheduleRepository.Replace(result.ProducedSchedule);
            producedVersion = result.ProducedSchedule.Version;
        }

        Log.RunCompleted(_logger, result.Run.RunId, request.AssetId, result.Run.Status, producedVersion ?? -1);

        return new ScheduleOptimizationOutcome(
            RunId: result.Run.RunId,
            Status: result.Run.Status,
            ProducedScheduleVersion: producedVersion,
            TerminationReason: result.Run.TerminationReason);
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static partial class Log
    {
        [LoggerMessage(EventId = 2003, Level = LogLevel.Information,
            Message = "Schedule-optimization run completed run_id={run_id} asset_id={asset_id} status={status} produced_version={produced_version}")]
        public static partial void RunCompleted(
            ILogger logger,
            Guid run_id,
            string asset_id,
            BatteryEms.Domain.OptimizationSolverStatus status,
            int produced_version);
    }
}
