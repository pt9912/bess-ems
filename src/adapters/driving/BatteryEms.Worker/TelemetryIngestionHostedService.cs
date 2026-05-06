using BatteryEms.Application.IO;
using BatteryEms.Application.Realtime;
using BatteryEms.Application.Time;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Worker;

// Pumps the configured IBatteryTelemetrySource into the snapshot store
// so ControlCycleUseCase has data to work on. The driven adapter
// (Modbus/MQTT/NoOp) decides what 'available' means; we just record
// every emission with the clock-current timestamp so the staleness
// guard in InMemorySnapshotStore measures observation latency.
//
// Adapter-side failures bubble out of ReadAsync as exceptions; the
// hosted service catches them, logs and restarts the iterator after a
// short backoff so a transient broker hiccup does not stop ingestion.
public sealed partial class TelemetryIngestionHostedService : BackgroundService
{
    private static readonly TimeSpan RestartBackoff = TimeSpan.FromSeconds(2);

    private readonly IBatteryTelemetrySource _source;
    private readonly ISnapshotStore _snapshots;
    private readonly IClock _clock;
    private readonly ILogger<TelemetryIngestionHostedService> _logger;

    public TelemetryIngestionHostedService(
        IBatteryTelemetrySource source,
        ISnapshotStore snapshots,
        IClock clock,
        ILogger<TelemetryIngestionHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _source = source;
        _snapshots = snapshots;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.IngestionStarted(_logger, _source.GetType().Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var telemetry in _source.ReadAsync(stoppingToken).ConfigureAwait(false))
                {
                    _snapshots.Update(telemetry, _clock.UtcNow);
                }
                // ReadAsync completed cleanly (NoOp source yields nothing
                // and exits) — back off briefly before re-attempting so
                // we don't spin in a tight loop.
                try
                {
                    await Task.Delay(RestartBackoff, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Adapter-level failures must not stop ingestion;
            catch (Exception ex) // log + back off + restart the iterator.
#pragma warning restore CA1031
            {
                Log.IngestionFailed(_logger, ex, _source.GetType().Name);
                try
                {
                    await Task.Delay(RestartBackoff, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        Log.IngestionStopped(_logger);
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static partial class Log
    {
        [LoggerMessage(EventId = 1910, Level = LogLevel.Information,
            Message = "Telemetry ingestion started source={source}")]
        public static partial void IngestionStarted(ILogger logger, string source);

        [LoggerMessage(EventId = 1911, Level = LogLevel.Information, Message = "Telemetry ingestion stopped")]
        public static partial void IngestionStopped(ILogger logger);

        [LoggerMessage(EventId = 1912, Level = LogLevel.Error,
            Message = "Telemetry ingestion failed source={source}; restarting after backoff")]
        public static partial void IngestionFailed(ILogger logger, Exception exception, string source);
    }
}
