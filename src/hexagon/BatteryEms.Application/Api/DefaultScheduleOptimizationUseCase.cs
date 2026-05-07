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
// IScheduleOptimizer for a result, replace the asset's schedule (when
// the solver produced a usable one) so the downstream IScheduleTracker
// / IDispatchOptimizer pair picks up the new version on the next
// regulation cycle, and finally append the OptimizationRun to the run
// repository (LH-PERSIST-007) so the audit log only mentions schedules
// that were actually persisted (review S2).
//
// Errors raised by the optimiser bubble out: appending a run record
// that wasn't actually produced would lie to the audit log. The API
// layer turns the bubble into the appropriate HTTP shape (RM-M2-OP-07).
//
// Concurrency scope (review S1): the per-key SemaphoreSlim guards the
// read-optimise-write block within a single host process. A multi-
// replica deployment with the Dapper schedule repository would still
// race two API instances both reading version v3 and both writing v4.
// The M3 follow-up (plan §Open RM-M2-OP-OPEN-05) lifts the guarantee
// into the persistence layer with an optimistic-concurrency check on
// `IScheduleRepository.Replace`.
//
// Lock-table lifetime (review C2): _locks accumulates one entry per
// distinct (AssetId, ScheduleType) ever optimised by this instance.
// For M2 with a stable IBatteryAssetRegistry the table plateaus at
// |assets| × |ScheduleType| ≈ a handful and never grows further. The
// native SemaphoreSlim handles are released on host shutdown via
// IDisposable so the kernel handles don't survive the process. The
// concern flips to "actively bounded" only if/when assets become
// ephemeral (multi-tenant rotation, per-test asset IDs); at that
// point an LRU/TTL eviction replaces the dictionary — captured as
// RM-M2-OP-OPEN-06 in the plan.
public sealed partial class DefaultScheduleOptimizationUseCase
    : IScheduleOptimizationUseCase, IDisposable
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

    // 0 = alive, 1 = disposed. Interlocked to make Dispose idempotent
    // even under unexpected concurrent-disposal races.
    private int _disposed;

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
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        // Drain the dictionary before disposing each semaphore so an
        // inflight ExecuteAsync that already passed the IsDisposed gate
        // can still Release on a snapshot reference. The native handle
        // backing each SemaphoreSlim is released here; the GC would
        // otherwise rely on a finaliser to close the kernel handle.
        foreach (var gate in _locks.Values)
        {
            gate.Dispose();
        }
        _locks.Clear();
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

        // Persistence order matters (review S2): replace the schedule
        // first, then append the run. If the replace throws (e.g. transient
        // DB error in the Dapper adapter) we never append a run that
        // would lie about a ProducedSchedule version that was never
        // persisted. The residual failure mode — Replace succeeds, Append
        // throws — leaves an active schedule without an audit run; an M3
        // follow-up wraps both writes in one transaction once the
        // persistence ports expose a shared connection scope (OPEN-05).
        int? producedVersion = null;
        if (result.ProducedSchedule is not null)
        {
            _scheduleRepository.Replace(result.ProducedSchedule);
            producedVersion = result.ProducedSchedule.Version;
        }

        await _runRepository.AppendAsync(result.Run, cancellationToken).ConfigureAwait(false);

        // Metrics fire after the run is durably persisted so a /metrics
        // scrape and the persisted run history can never disagree on
        // counts (LH-OPT-009 audit-stance: a run that wasn't appended
        // didn't happen, so it shouldn't be counted either).
        _metrics.Record(result.Run);

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
