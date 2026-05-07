using System.Collections.Concurrent;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Application.Api;

// Wires the schedule-optimisation pipeline together: resolve identity
// (market bid area + next version) from the existing Schedule for
// (AssetId, ScheduleType) under a per-key lock so two concurrent
// optimise calls cannot race on the version, ask the driven
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
    // M2 default: every host is single-bid-area "DE-LU"; multi-area
    // hosts will replace this with a configured option in M3 once the
    // import path teaches operators to seed the bid area before any
    // optimisation runs.
    private const string DefaultMarketBidArea = "DE-LU";

    private readonly IScheduleOptimizer _optimizer;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IOptimizationRunRepository _runRepository;
    private readonly IOptimizationRunMetrics _metrics;
    private readonly ILogger<DefaultScheduleOptimizationUseCase> _logger;

    // Per-(asset, type) serialisation guards the read-optimise-write
    // sequence so two parallel calls cannot read the same base version
    // and overwrite each other's produced schedule (review #1).
    private readonly ConcurrentDictionary<(string AssetId, ScheduleType Type), SemaphoreSlim> _locks = new();

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
        ScheduleOptimizationInputs inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var key = (inputs.AssetId, inputs.ScheduleType);
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecuteUnderLockAsync(inputs, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ScheduleOptimizationOutcome> ExecuteUnderLockAsync(
        ScheduleOptimizationInputs inputs,
        CancellationToken cancellationToken)
    {
        // Inside the lock: read the latest schedule for (asset, type),
        // derive identity, build the full request and call the optimiser.
        var existing = _scheduleRepository.FindActive(inputs.AssetId, inputs.ScheduleType);
        var marketBidArea = existing?.MarketBidArea ?? DefaultMarketBidArea;
        var baseVersion = existing?.Version ?? 0;

        var request = new ScheduleOptimizationRequest(
            assetId: inputs.AssetId,
            scheduleType: inputs.ScheduleType,
            asset: inputs.Asset,
            horizonStart: inputs.HorizonStart,
            horizonEnd: inputs.HorizonEnd,
            timeStep: inputs.TimeStep,
            marketBidArea: marketBidArea,
            baseScheduleVersion: baseVersion,
            pricesPerStep: inputs.PricesPerStep,
            priceUnit: inputs.PriceUnit,
            inputs: inputs.Inputs);

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
