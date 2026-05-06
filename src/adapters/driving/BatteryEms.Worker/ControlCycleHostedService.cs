using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.IO;
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
    private readonly ILogger<ControlCycleHostedService> _logger;
    private readonly WorkerOptions _options;

    public ControlCycleHostedService(
        IControlCycleUseCase cycle,
        IBatteryAssetRegistry assets,
        IBatteryCommandSink sink,
        ICommandRepository commandRepository,
        IControlCycleMetrics metrics,
        IClock clock,
        ILogger<ControlCycleHostedService> logger,
        IOptions<WorkerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(commandRepository);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _cycle = cycle;
        _assets = assets;
        _sink = sink;
        _commandRepository = commandRepository;
        _metrics = metrics;
        _clock = clock;
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
        foreach (var asset in _assets.GetAll())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await ExecuteForAssetAsync(asset.AssetId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteForAssetAsync(string assetId, CancellationToken cancellationToken)
    {
        try
        {
            var command = await _cycle.ExecuteAsync(assetId, cancellationToken).ConfigureAwait(false);
            var dispatch = await _sink.WriteAsync(command, cancellationToken).ConfigureAwait(false);
            await _commandRepository.AppendAsync(command, dispatch, cancellationToken).ConfigureAwait(false);
            if (!dispatch.Success)
            {
                _metrics.IncrementCommunicationError(assetId, "command-sink");
                Log.DispatchFailed(_logger, assetId, dispatch.Reason);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Adapter / use-case crashes must not kill the loop — record
        catch (Exception ex) // and continue so the next tick has a chance to recover.
#pragma warning restore CA1031
        {
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
