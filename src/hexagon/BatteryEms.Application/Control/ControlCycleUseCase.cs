using System.Collections.Concurrent;
using System.Diagnostics;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Realtime;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Application.Control;

public sealed partial class ControlCycleUseCase : IControlCycleUseCase
{
    private readonly IBatteryAssetRegistry _assets;
    private readonly ISnapshotStore _snapshots;
    private readonly IScheduleTracker _scheduleTracker;
    private readonly IOperatorStopRegistry _operatorStops;
    private readonly IDispatchOptimizer _optimizer;
    private readonly IClock _clock;
    private readonly IControlCycleMetrics _metrics;
    private readonly ILogger<ControlCycleUseCase> _logger;
    private readonly ControlCycleOptions _options;
    // RM-M3-05 driven port for the Constraint+Ramp pipeline. Defaults
    // to ManagedControlKernel when DI does not override; the HOSt
    // wires NativeFallbackControlKernel when NativeControl:Enabled is
    // true, but the cycle itself sees only the IControlKernel surface.
    private readonly IControlKernel _kernel;

    private readonly ConcurrentDictionary<string, (double Power, DateTimeOffset At)> _previous =
        new(StringComparer.Ordinal);

    public ControlCycleUseCase(
        IBatteryAssetRegistry assets,
        ISnapshotStore snapshots,
        IScheduleTracker scheduleTracker,
        IOperatorStopRegistry operatorStops,
        IDispatchOptimizer optimizer,
        IClock clock,
        IControlCycleMetrics metrics,
        ILogger<ControlCycleUseCase> logger,
        ControlCycleOptions options,
        IControlKernel? kernel = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(scheduleTracker);
        ArgumentNullException.ThrowIfNull(operatorStops);
        ArgumentNullException.ThrowIfNull(optimizer);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _assets = assets;
        _snapshots = snapshots;
        _scheduleTracker = scheduleTracker;
        _operatorStops = operatorStops;
        _optimizer = optimizer;
        _clock = clock;
        _metrics = metrics;
        _logger = logger;
        _options = options;
        _kernel = kernel ?? new ManagedControlKernel();
    }

    public async Task<BatteryCommand> ExecuteAsync(string assetId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await ExecuteCoreAsync(assetId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordCycleDuration(assetId, stopwatch.Elapsed);
        }
    }

    private async Task<BatteryCommand> ExecuteCoreAsync(string assetId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // LH-API-006 short-circuit: an active operator stop overrides
        // every other input — telemetry, schedule and optimiser are not
        // even consulted. Reason carries the operator's text so the
        // audit trail stays useful, prefixed so observability can grep
        // for operator-stop events without parsing free-form reasons.
        var operatorStop = _operatorStops.Find(assetId);
        if (operatorStop is not null)
        {
            var operatorStopReason = $"operator-stop:{operatorStop.Reason}";
            return EmitSafeStop(assetId, now, operatorStopReason, CommandSource.Operator, decision: "operator-stop");
        }

        var asset = _assets.Find(assetId);
        if (asset is null)
        {
            return EmitSafeStop(assetId, now, "asset-not-registered", CommandSource.Fallback, decision: "asset-not-registered");
        }

        var snapshot = _snapshots.GetLatest(assetId, now);
        if (snapshot is null)
        {
            _metrics.IncrementInvalidSnapshot(assetId, "no-snapshot");
            return EmitSafeStop(assetId, now, "no-snapshot", CommandSource.Fallback, decision: "no-snapshot");
        }

        if (!snapshot.Quality.IsUsableForControl)
        {
            _metrics.IncrementInvalidSnapshot(assetId, snapshot.Quality.Reason);
            return EmitSafeStop(assetId, now, snapshot.Quality.Reason, CommandSource.Fallback, decision: "snapshot-unusable");
        }

        if (!snapshot.Telemetry.Available)
        {
            return EmitSafeStop(assetId, now, "asset-unavailable", CommandSource.Fallback, decision: "asset-unavailable");
        }

        var commitments = _scheduleTracker.GetActiveCommitments(assetId, now);
        var dispatch = await _optimizer.OptimizeAsync(
            new DispatchRequest(assetId, now, asset, snapshot.Telemetry, commitments),
            cancellationToken).ConfigureAwait(false);

        if (!dispatch.IsValid)
        {
            return EmitSafeStop(assetId, now, dispatch.Reason, CommandSource.Fallback, decision: "dispatch-invalid");
        }

        // RM-M3-05 precheck: a non-finite dispatch target would
        // otherwise propagate into the kernel and either crash the
        // managed Constraint comparisons or trigger a native
        // non-finite status. Treat it as a safe-stop so the kernel
        // never sees a NaN/Inf input.
        if (!double.IsFinite(dispatch.TargetActivePowerKw))
        {
            return EmitSafeStop(assetId, now, "dispatch-target-not-finite",
                CommandSource.Fallback, decision: "dispatch-target-not-finite");
        }

        double? previousPower = null;
        var elapsed = TimeSpan.Zero;
        if (_previous.TryGetValue(assetId, out var prev))
        {
            previousPower = prev.Power;
            elapsed = now - prev.At;
        }

        var kernelInput = new KernelInput(
            Asset: asset,
            Telemetry: snapshot.Telemetry,
            DispatchTargetActivePowerKw: dispatch.TargetActivePowerKw,
            PreviousActivePowerKw: previousPower,
            TimeSinceLastCommand: elapsed);
        var kernelResult = _kernel.Compute(kernelInput);

        _previous[assetId] = (kernelResult.ActivePowerKw, now);

        var mode = kernelResult.ActivePowerKw switch
        {
            > 0 => CommandMode.Discharge,
            < 0 => CommandMode.Charge,
            _ => CommandMode.Idle,
        };

        // When neither Constraint nor Ramp limited, surface the
        // dispatch reason rather than the kernel's `within-limits`
        // boilerplate so operators still see WHY the optimiser chose
        // this setpoint (carries through from the M2 cycle behaviour).
        var reason = kernelResult.WasLimited ? kernelResult.Reason : dispatch.Reason;

        var command = new BatteryCommand(
            CommandId: $"ctrl-{now.ToUnixTimeMilliseconds()}-{assetId}",
            Timestamp: now,
            AssetId: assetId,
            Mode: mode,
            ActivePowerKw: kernelResult.ActivePowerKw,
            ReactivePowerKvar: 0,
            ValidUntil: now + _options.SafeFallbackValidity,
            Reason: reason,
            Source: CommandSource.Optimization);

        _metrics.SetActivePowerKw(assetId, command.ActivePowerKw);
        _metrics.SetSocPercent(assetId, snapshot.Telemetry.SocPercent);
        _metrics.RecordCommandLatency(assetId, now - snapshot.Telemetry.Timestamp);

        // LH-MON-001: structured log carries the canonical observability
        // fields (asset_id, component, decision, reason) so JSON-console
        // consumers get one line per control cycle without parsing a
        // free-form message string.
        Log.CycleAcceptedCommand(_logger, assetId, mode, command.ActivePowerKw, reason);
        return command;
    }

    private BatteryCommand EmitSafeStop(
        string assetId,
        DateTimeOffset now,
        string reason,
        CommandSource source,
        string decision)
    {
        var command = BatteryCommand.SafeStop(assetId, now, _options.SafeFallbackValidity, reason, source);
        _metrics.RecordSafeStop(assetId, reason);
        _metrics.SetActivePowerKw(assetId, command.ActivePowerKw);
        Log.CycleSafeStop(_logger, assetId, decision, reason);
        return command;
    }

    // High-performance logging via LoggerMessage source generator. Keeps
    // structured fields (asset_id, decision, reason) addressable for the
    // JSON console formatter without per-call string allocations.
    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1701,
            Level = LogLevel.Information,
            Message = "Control cycle emitted command {asset_id} mode={mode} power_kw={power_kw} reason={reason}")]
        public static partial void CycleAcceptedCommand(
            ILogger logger,
            string asset_id,
            CommandMode mode,
            double power_kw,
            string reason);

        [LoggerMessage(
            EventId = 1702,
            Level = LogLevel.Warning,
            Message = "Control cycle safe-stop {asset_id} decision={decision} reason={reason}")]
        public static partial void CycleSafeStop(
            ILogger logger,
            string asset_id,
            string decision,
            string reason);
    }
}
