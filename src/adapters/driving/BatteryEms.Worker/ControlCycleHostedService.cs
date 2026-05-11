using System.Diagnostics;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.IO;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Time;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryEms.Worker;

// Drives IControlCycleUseCase at WorkerOptions.CycleInterval for every
// asset the registry currently knows about. Errors are logged and counted
// (IControlCycleMetrics) but never bubble out — the loop must keep running
// so the next tick has a chance to recover.
//
// The hosted service intentionally stays thin: scheduling, dispatch and
// persistence are Application concerns; we only orchestrate the per-tick
// fan-out across assets.
public sealed partial class ControlCycleHostedService : BackgroundService
{
    private readonly IControlCycleUseCase _cycle;
    private readonly IBatteryAssetRegistry _assets;
    private readonly IBatteryCommandSink _sink;
    private readonly ICommandRepository _commandRepository;
    private readonly IControlCycleMetrics _metrics;
    private readonly IClock _clock;
    private readonly ITimebaseHealthObserver _timebaseObserver;
    private readonly ILogger<ControlCycleHostedService> _logger;
    private readonly WorkerOptions _options;

    // Plan-RM-M4-03 §144 Finding-2-Wiring: tick-level Clock-Anomaly-
    // Detection füttert die `ITimebaseHealthObserver`-Maschine. Wir
    // halten den Timestamp des vorherigen Ticks, vergleichen pro Tick
    // gegen den erwarteten `CycleInterval`, und melden Anomalien als
    // Violation. Der State wird per `lock` geschützt damit ein
    // potentieller späterer Concurrent-Cycle (Multi-Asset-Future) keine
    // Race-Beobachtung produziert.
    private readonly object _timebaseGate = new();
    private DateTimeOffset? _previousTickTimestamp;

    public ControlCycleHostedService(
        IControlCycleUseCase cycle,
        IBatteryAssetRegistry assets,
        IBatteryCommandSink sink,
        ICommandRepository commandRepository,
        IControlCycleMetrics metrics,
        IClock clock,
        ITimebaseHealthObserver timebaseObserver,
        ILogger<ControlCycleHostedService> logger,
        IOptions<WorkerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(commandRepository);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(timebaseObserver);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _cycle = cycle;
        _assets = assets;
        _sink = sink;
        _commandRepository = commandRepository;
        _metrics = metrics;
        _clock = clock;
        _timebaseObserver = timebaseObserver;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.LoopStarted(_logger, _options.CycleInterval.TotalMilliseconds);

        using var timer = new PeriodicTimer(_options.CycleInterval);
        try
        {
            do
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — host is stopping the service.
        }

        Log.LoopStopped(_logger);
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        ObserveTimebaseClock();

        foreach (var asset in _assets.GetAll())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await ExecuteForAssetAsync(asset.AssetId, cancellationToken).ConfigureAwait(false);
        }
    }

    // Plan-RM-M4-03 §144 Finding-2-Wiring: pro Tick Clock-Anomalien
    // erkennen und an die `ITimebaseHealthObserver`-Maschine melden.
    // Klassifikation in `ComputeTimebaseViolation`; hier nur State-
    // Übergang + Observer-Call. State per `lock` damit eine spätere
    // Multi-Asset-Concurrent-Cycle-Variante keine Race-Beobachtung
    // produziert.
    private void ObserveTimebaseClock()
    {
        var now = _clock.UtcNow;
        bool violation;
        lock (_timebaseGate)
        {
            violation = ComputeTimebaseViolation(
                _previousTickTimestamp, now, _options.CycleInterval);
            _previousTickTimestamp = now;
        }
        _timebaseObserver.Observe(violation);
    }

    // Pure: klassifiziert ein Tick-zu-Tick-Delta als Violation oder
    // stable. Eine Violation ist eines von:
    //   (a) Delta < 0  → Clock-Rückspring (NTP-Step rückwärts oder Host-
    //       Suspend mit fehlerhafter Resync).
    //   (b) Delta > 2 × CycleInterval → ausgelassener Tick (Host stalled,
    //       PeriodicTimer hat überrollt, Clock sprang vorwärts).
    // Drift im Bereich [0, 2×Interval] gilt als stable; das toleriert
    // normalen Scheduling-Jitter (PeriodicTimer kann unter Last leicht
    // verspätet feuern). Erster Tick nach Boot hat keinen Vergleichs-
    // Anker → wird als stable gemeldet, der Zustand bleibt im Initial-
    // Healthy.
    internal static bool ComputeTimebaseViolation(
        DateTimeOffset? previousTickTimestamp,
        DateTimeOffset currentTickTimestamp,
        TimeSpan cycleInterval)
    {
        if (previousTickTimestamp is not { } previous)
        {
            return false;
        }
        var delta = currentTickTimestamp - previous;
        return delta < TimeSpan.Zero || delta > cycleInterval * 2;
    }

    private async Task ExecuteForAssetAsync(string assetId, CancellationToken cancellationToken)
    {
        // RM-M2-06 / LH-MON-003: outer span covers snapshot read,
        // dispatch decision, limiter chain and command emission. Span
        // attributes mirror LH-MON-001 structured-log fields so traces,
        // logs and metrics share a vocabulary.
        using var cycleActivity = BessActivitySources.ControlCycle.StartActivity(
            "bess.control_cycle.execute");
        cycleActivity?.SetTag(BessActivityTags.AssetId, assetId);
        try
        {
            var command = await _cycle.ExecuteAsync(assetId, cancellationToken).ConfigureAwait(false);
            cycleActivity?.SetTag(BessActivityTags.CommandMode, command.Mode.ToString());
            cycleActivity?.SetTag(BessActivityTags.PowerKw, command.ActivePowerKw);
            cycleActivity?.SetTag(BessActivityTags.CommandReason, command.Reason);

            // RM-M2-06: child span around the sink write so failed
            // dispatches surface in the trace as a distinct span with
            // an Error status, not just as a log line on the parent.
            CommandDispatchResult dispatch;
            using (var dispatchActivity = BessActivitySources.CommandDispatch.StartActivity(
                "bess.command_dispatch.write"))
            {
                dispatchActivity?.SetTag(BessActivityTags.AssetId, assetId);
                dispatchActivity?.SetTag(BessActivityTags.CommandMode, command.Mode.ToString());
                dispatchActivity?.SetTag(BessActivityTags.PowerKw, command.ActivePowerKw);
                dispatch = await _sink.WriteAsync(command, cancellationToken).ConfigureAwait(false);
                dispatchActivity?.SetTag(BessActivityTags.DispatchSuccess, dispatch.Success);
                dispatchActivity?.SetTag(BessActivityTags.DispatchReason, dispatch.Reason);
                dispatchActivity?.SetStatus(
                    dispatch.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
                    dispatch.Success ? null : dispatch.Reason);
            }

            await _commandRepository.AppendAsync(command, dispatch, cancellationToken).ConfigureAwait(false);
            if (!dispatch.Success)
            {
                _metrics.IncrementCommunicationError(assetId, "command-sink");
                Log.DispatchFailed(_logger, assetId, dispatch.Reason);
            }
            cycleActivity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Adapter / use-case crashes must not kill the loop — record
        catch (Exception ex) // and continue so the next tick has a chance to recover.
#pragma warning restore CA1031
        {
            cycleActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _metrics.IncrementCommunicationError(assetId, "control-cycle");
            Log.TickFailed(_logger, ex, assetId);
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static partial class Log
    {
        [LoggerMessage(EventId = 1901, Level = LogLevel.Information,
            Message = "Control-cycle worker started cycle_interval_ms={cycle_interval_ms}")]
        public static partial void LoopStarted(ILogger logger, double cycle_interval_ms);

        [LoggerMessage(EventId = 1902, Level = LogLevel.Information, Message = "Control-cycle worker stopped")]
        public static partial void LoopStopped(ILogger logger);

        [LoggerMessage(EventId = 1903, Level = LogLevel.Warning,
            Message = "Command-sink dispatch failed asset_id={asset_id} reason={reason}")]
        public static partial void DispatchFailed(ILogger logger, string asset_id, string reason);

        [LoggerMessage(EventId = 1904, Level = LogLevel.Error,
            Message = "Control-cycle tick failed asset_id={asset_id}")]
        public static partial void TickFailed(ILogger logger, Exception exception, string asset_id);
    }
}
