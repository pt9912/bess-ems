using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Realtime;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryEms.Application.Tests;

// RM-M2-10 / LH-TEST-004: minimal replay harness around
// ControlCycleUseCase. Construction wires deterministic dependencies
// (FakeClock, InMemorySnapshotStore, InMemoryScheduleRepository,
// NoOp metrics) so a recorded telemetry sequence drives the same
// regelzyklus path as production with reproducible outputs. The
// "fixture format" is the TelemetryReplayRecord shape — a JSON file
// loader is deferred to a follow-up slice (see roadmap RM-M2-10
// carve-outs).
//
// Per-replay state isolation: each harness holds a private clock,
// snapshot store, schedule repo and a fresh ControlCycleUseCase, so
// the use case's internal _previous power dictionary starts empty.
// Reproducibility tests build TWO harnesses for the same fixture and
// assert the output sequences are bit-exact identical.
internal sealed class TelemetryReplayHarness
{
    private readonly FakeClock _clock = new();
    private readonly InMemorySnapshotStore _snapshots = new(TimeSpan.FromSeconds(10));
    private readonly ControlCycleUseCase _cycle;

    public TelemetryReplayHarness(
        BatteryAsset asset,
        IDispatchOptimizer optimizer,
        IReadOnlyList<Schedule>? preSeededSchedules = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(optimizer);

        var assets = new InMemoryBatteryAssetRegistry(new[] { asset });
        var schedules = preSeededSchedules is null
            ? new InMemoryScheduleRepository()
            : new InMemoryScheduleRepository(preSeededSchedules);
        var tracker = new DefaultScheduleTracker(schedules);
        var operatorStops = new InMemoryOperatorStopRegistry();

        _cycle = new ControlCycleUseCase(
            assets,
            _snapshots,
            tracker,
            operatorStops,
            optimizer,
            _clock,
            NoOpControlCycleMetrics.Instance,
            NullLogger<ControlCycleUseCase>.Instance,
            ControlCycleOptions.Default);
    }

    public async Task<IReadOnlyList<BatteryCommand>> RunAsync(
        string assetId,
        IReadOnlyList<TelemetryReplayRecord> records,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentNullException.ThrowIfNull(records);

        var commands = new List<BatteryCommand>(records.Count);
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            _clock.UtcNow = record.Timestamp;
            if (record.Telemetry is not null)
            {
                _snapshots.Update(record.Telemetry, record.ReceivedAt ?? record.Timestamp);
            }
            commands.Add(await _cycle.ExecuteAsync(assetId, cancellationToken).ConfigureAwait(false));
        }
        return commands;
    }
}

// One row of the telemetry-replay fixture. Telemetry == null means
// "skip pumping the snapshot store this tick" — useful to model a
// missing-snapshot path or a stale-after-no-update scenario.
internal sealed record TelemetryReplayRecord(
    DateTimeOffset Timestamp,
    BatteryTelemetry? Telemetry,
    DateTimeOffset? ReceivedAt = null);
