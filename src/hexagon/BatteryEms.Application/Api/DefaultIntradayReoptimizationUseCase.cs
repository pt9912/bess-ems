using System.Collections.Concurrent;
using System.Diagnostics;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Application.Api;

// RM-M4-01: residual-horizon Intraday reoptimisation. Reads the
// existing Intraday schedule for the asset, splits it into past
// windows (preserved verbatim) and future windows (re-optimised by
// the same IScheduleOptimizer the day-ahead pipeline uses), then
// replaces the schedule under the per-(asset, type) lock + CAS
// path established by RM-M3-FUP-02.
//
// Design decisions (plan-RM-M4 RM-M4-01):
//   D-01: an existing Intraday baseline is required; no implicit
//         cold-start from Day-Ahead. Missing baseline ⇒ Failed run
//         with TerminationCode "intraday-baseline-missing". The
//         cold-start mechanic itself is follow-up F-01 in
//         note-RM-M4-followups.md.
//   D-02: ResidualStart must align to a window boundary of the
//         existing schedule. Misalignment ⇒ Failed run with
//         TerminationCode "residual-start-not-aligned". Snap-to-
//         boundary toleration is follow-up F-02.
//   D-03: Replace stays destructive per the M1 contract; past-
//         windows are part of the new combined Schedule (same
//         row), no historical retention.
//   D-04: The HTTP layer binds this synchronously (POST
//         /markets/intraday/reoptimize); async-job is the global
//         RM-M2-OP-OPEN-04 carve-out.
public sealed partial class DefaultIntradayReoptimizationUseCase
    : IIntradayReoptimizationUseCase, IDisposable
{
    private readonly IScheduleOptimizer _optimizer;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IReserveRepository _reserveRepository;
    private readonly IOptimizationRunRepository _runRepository;
    private readonly IOptimizationRunMetrics _metrics;
    private readonly IClock _clock;
    private readonly ILogger<DefaultIntradayReoptimizationUseCase> _logger;

    // Per-(asset, Intraday) lock — same shape as the day-ahead use
    // case so two reopt calls on the same asset cannot race on the
    // existing-schedule version. The key is `string AssetId` because
    // the ScheduleType is fixed to Intraday for this use case.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    private int _disposed;

    public DefaultIntradayReoptimizationUseCase(
        IScheduleOptimizer optimizer,
        IScheduleRepository scheduleRepository,
        IReserveRepository reserveRepository,
        IOptimizationRunRepository runRepository,
        IOptimizationRunMetrics metrics,
        IClock clock,
        ILogger<DefaultIntradayReoptimizationUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(optimizer);
        ArgumentNullException.ThrowIfNull(scheduleRepository);
        ArgumentNullException.ThrowIfNull(reserveRepository);
        ArgumentNullException.ThrowIfNull(runRepository);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _optimizer = optimizer;
        _scheduleRepository = scheduleRepository;
        _reserveRepository = reserveRepository;
        _runRepository = runRepository;
        _metrics = metrics;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ScheduleOptimizationOutcome> ExecuteAsync(
        IntradayReoptimizationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        using var activity = BessActivitySources.ScheduleOptimization.StartActivity(
            "bess.intraday_reoptimization.run");
        activity?.SetTag(BessActivityTags.AssetId, command.AssetId);

        var gate = _locks.GetOrAdd(command.AssetId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var outcome = await ExecuteUnderLockAsync(command, cancellationToken).ConfigureAwait(false);
            activity?.SetTag(BessActivityTags.RunId, outcome.RunId);
            activity?.SetTag(BessActivityTags.SolverStatus, outcome.Status.ToString());
            activity?.SetTag(BessActivityTags.TerminationReason, outcome.TerminationReason);
            if (outcome.ProducedScheduleVersion is { } v)
            {
                activity?.SetTag(BessActivityTags.ProducedScheduleVersion, v);
            }
            activity?.SetStatus(ActivityStatusCode.Ok);
            return outcome;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
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
        foreach (var gate in _locks.Values)
        {
            gate.Dispose();
        }
        _locks.Clear();
    }

    private async Task<ScheduleOptimizationOutcome> ExecuteUnderLockAsync(
        IntradayReoptimizationCommand command,
        CancellationToken cancellationToken)
    {
        var existing = _scheduleRepository.FindActive(command.AssetId, ScheduleType.Intraday);

        // D-01: no implicit cold-start.
        if (existing is null)
        {
            var baselineMissingRun = BuildPrecheckFailedRun(
                command,
                terminationCode: "intraday-baseline-missing",
                terminationDetail: $"asset={command.AssetId}",
                inputs: Array.Empty<ScheduleReference>());
            return await PersistAndReturnAsync(command, baselineMissingRun, producedVersion: null, cancellationToken)
                .ConfigureAwait(false);
        }

        // D-02: residualStart must coincide with a window boundary of
        // the existing schedule. Strictly: not strictly-inside any
        // half-open window (window.Start < residualStart < window.End).
        if (!IsAtWindowBoundary(existing, command.ResidualStart))
        {
            var alignmentRun = BuildPrecheckFailedRun(
                command,
                terminationCode: "residual-start-not-aligned",
                terminationDetail: System.FormattableString.Invariant(
                    $"residualStart={command.ResidualStart:O}, schedule_v{existing.Version}"),
                inputs: new[] { new ScheduleReference(existing.AssetId, existing.Type, existing.Version) });
            return await PersistAndReturnAsync(command, alignmentRun, producedVersion: null, cancellationToken)
                .ConfigureAwait(false);
        }

        // Past windows: end at or before residualStart (preserved
        // verbatim). Future windows live on the residual-horizon LP
        // side and get fully replaced by the optimiser's output.
        var pastWindows = existing.Windows
            .Where(w => w.End <= command.ResidualStart)
            .ToArray();

        var reserves = _reserveRepository.FindActive(
            command.AssetId, command.ResidualStart, command.HorizonEnd);

        var request = new ScheduleOptimizationRequest(
            command: command.Inner,
            marketBidArea: existing.MarketBidArea,
            baseScheduleVersion: existing.Version,
            reserves: reserves);

        var result = await _optimizer.OptimizeAsync(request, cancellationToken).ConfigureAwait(false);

        // No usable LP solution → no Replace. The existing schedule
        // stays active; the Failed run is persisted for audit.
        if (result.ProducedSchedule is null)
        {
            return await PersistAndReturnAsync(command, result.Run, producedVersion: null, cancellationToken)
                .ConfigureAwait(false);
        }

        // Combine past + new windows. The combined Schedule keeps the
        // existing.MarketBidArea and gets version = existing.Version + 1
        // (consistent with the optimiser's ProducedSchedule.Version).
        var combinedWindows = new List<ScheduleWindow>(pastWindows.Length + result.ProducedSchedule.Windows.Count);
        combinedWindows.AddRange(pastWindows);
        combinedWindows.AddRange(result.ProducedSchedule.Windows);
        var combined = new Schedule(
            assetId: existing.AssetId,
            type: ScheduleType.Intraday,
            marketBidArea: existing.MarketBidArea,
            version: result.ProducedSchedule.Version,
            windows: combinedWindows);

        int? producedVersion = null;
        var runToPersist = result.Run;
        try
        {
            _scheduleRepository.Replace(combined, expectedBaseVersion: existing.Version);
            producedVersion = combined.Version;
        }
        catch (ScheduleConcurrencyConflictException conflict)
        {
            // RM-M3-FUP-02: a sibling replica advanced the version
            // between FindActive and Replace. The optimal solution
            // never reached the store, so we synthesize a Failed run
            // with the conflict reason and persist THAT.
            runToPersist = BuildConcurrencyConflictRun(request, result.Run, conflict);
            producedVersion = null;
        }

        return await PersistAndReturnAsync(command, runToPersist, producedVersion, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsAtWindowBoundary(Schedule existing, DateTimeOffset residualStart)
    {
        // residualStart aligns iff it is NOT strictly inside any
        // window's half-open interior. Equivalently: no window has
        // `Start < residualStart < End`. residualStart equal to a
        // Start or an End counts as aligned.
        foreach (var window in existing.Windows)
        {
            if (window.Start < residualStart && residualStart < window.End)
            {
                return false;
            }
        }
        return true;
    }

    private async Task<ScheduleOptimizationOutcome> PersistAndReturnAsync(
        IntradayReoptimizationCommand command,
        OptimizationRun run,
        int? producedVersion,
        CancellationToken cancellationToken)
    {
        await _runRepository.AppendAsync(run, cancellationToken).ConfigureAwait(false);
        _metrics.Record(run);

        Log.RunCompleted(_logger, run.RunId, command.AssetId, run.Status, producedVersion ?? -1);

        return new ScheduleOptimizationOutcome(
            RunId: run.RunId,
            Status: run.Status,
            ProducedScheduleVersion: producedVersion,
            TerminationReason: run.TerminationReason);
    }

    // Synthesizes a Failed OptimizationRun for a precheck failure
    // (D-01 baseline-missing, D-02 alignment). The horizon is the
    // residual one the caller asked for; runtime is zero (no solver
    // was invoked); SolverName attributes the failure to the
    // reopt-precheck guard so dashboards that group by SolverName
    // don't mis-count a precheck reject as a solver failure.
    private OptimizationRun BuildPrecheckFailedRun(
        IntradayReoptimizationCommand command,
        string terminationCode,
        string terminationDetail,
        IReadOnlyList<ScheduleReference> inputs)
    {
        return new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: command.AssetId,
            solverName: "intraday-reopt-precheck",
            status: OptimizationSolverStatus.Failed,
            horizonStart: command.ResidualStart,
            horizonEnd: command.HorizonEnd,
            timeStep: command.TimeStep,
            objectiveValue: 0,
            objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            solverRuntime: TimeSpan.Zero,
            terminationCode: terminationCode,
            terminationDetail: terminationDetail,
            createdAt: _clock.UtcNow,
            inputs: inputs,
            producedSchedule: null);
    }

    // Same shape as the day-ahead-side helper (RM-M3-FUP-02 m-2):
    // SolverName attributes the conflict to the persistence-side
    // CAS guard, not the optimiser. Inputs reuse the optimiser-side
    // inputs (the prior Schedule that *was* read).
    private OptimizationRun BuildConcurrencyConflictRun(
        ScheduleOptimizationRequest request,
        OptimizationRun originalRun,
        ScheduleConcurrencyConflictException conflict)
    {
        var detail = $"expected={conflict.ExpectedBaseVersion},actual={conflict.ActualVersion}";
        return new OptimizationRun(
            runId: Guid.NewGuid(),
            assetId: request.AssetId,
            solverName: "schedule-cas-guard",
            status: OptimizationSolverStatus.Failed,
            horizonStart: originalRun.HorizonStart,
            horizonEnd: originalRun.HorizonEnd,
            timeStep: originalRun.TimeStep,
            objectiveValue: 0,
            objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            solverRuntime: originalRun.SolverRuntime,
            terminationCode: "concurrent-version-conflict",
            terminationDetail: detail,
            createdAt: _clock.UtcNow,
            inputs: originalRun.Inputs,
            producedSchedule: null);
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static partial class Log
    {
        [LoggerMessage(EventId = 2004, Level = LogLevel.Information,
            Message = "Intraday-reopt run completed run_id={run_id} asset_id={asset_id} status={status} produced_version={produced_version}")]
        public static partial void RunCompleted(
            ILogger logger,
            Guid run_id,
            string asset_id,
            BatteryEms.Domain.OptimizationSolverStatus status,
            int produced_version);
    }
}
