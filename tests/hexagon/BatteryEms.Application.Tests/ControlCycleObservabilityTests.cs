using BatteryEms.Adapters.Optimization;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Realtime;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class ControlCycleObservabilityTests
{
    [Fact]
    public async Task Cycle_records_duration_for_every_invocation_including_safe_stop_paths()
    {
        var (cycle, _, metrics, _) = Build();
        await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        // Duration is observed even though the cycle hits the no-snapshot
        // safe-stop path — LH-MON-002 covers fast paths too.
        var duration = Assert.Single(metrics.CycleDurations);
        Assert.Equal("asset-1", duration.AssetId);
        Assert.True(duration.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Missing_snapshot_increments_invalid_snapshot_counter()
    {
        var (cycle, _, metrics, _) = Build();

        await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Contains(metrics.InvalidSnapshots, e => e.AssetId == "asset-1" && e.Reason == "no-snapshot");
        Assert.Contains(metrics.SafeStops, e => e.AssetId == "asset-1" && e.Reason == "no-snapshot");
    }

    [Fact]
    public async Task Stale_snapshot_increments_invalid_snapshot_counter_with_quality_reason()
    {
        var (cycle, snapshots, metrics, clock) = Build();
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);
        clock.UtcNow = TestFixtures.Now + TimeSpan.FromSeconds(30);

        await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        var entry = Assert.Single(metrics.InvalidSnapshots);
        Assert.Equal("asset-1", entry.AssetId);
        Assert.Contains("snapshot-aged", entry.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Operator_stop_records_safe_stop_with_prefixed_reason_and_no_invalid_snapshot()
    {
        var (cycle, snapshots, metrics, _) = Build();
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);
        // operator-stop short-circuits before any snapshot work, so the
        // invalid-snapshot counter must stay flat.
        var stops = new InMemoryOperatorStopRegistry();
        stops.Activate(new OperatorStopState("asset-1", "op-1", "evac-drill", TestFixtures.Now));
        var localCycle = BuildCycle(stops, metrics);

        await localCycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Empty(metrics.InvalidSnapshots);
        Assert.Contains(metrics.SafeStops, e => e.AssetId == "asset-1" && e.Reason == "operator-stop:evac-drill");
    }

    [Fact]
    public async Task Accepted_command_records_power_soc_and_command_latency()
    {
        var (cycle, snapshots, metrics, clock) = Build(new FixedOptimizer(25));
        // Telemetry is observed at TestFixtures.Now; the cycle then runs
        // 150 ms later — RecordCommandLatency must surface the gap.
        snapshots.Update(TestFixtures.CreateTelemetry(socPercent: 55), TestFixtures.Now);
        clock.UtcNow = TestFixtures.Now + TimeSpan.FromMilliseconds(150);

        await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(25, metrics.LatestActivePowerKw["asset-1"]);
        Assert.Equal(55, metrics.LatestSocPercent["asset-1"]);
        var latency = Assert.Single(metrics.CommandLatencies);
        Assert.Equal("asset-1", latency.AssetId);
        Assert.Equal(TimeSpan.FromMilliseconds(150), latency.Latency);
        Assert.Empty(metrics.SafeStops);
    }

    [Fact]
    public async Task Asset_not_registered_records_safe_stop_without_snapshot_counter()
    {
        var assets = new InMemoryBatteryAssetRegistry();
        var snapshots = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        var metrics = new SpyControlCycleMetrics();
        var clock = new FakeClock();
        var cycle = new ControlCycleUseCase(
            assets,
            snapshots,
            new DefaultScheduleTracker(new InMemoryScheduleRepository()),
            new InMemoryOperatorStopRegistry(),
            new NoOpDispatchOptimizer(),
            clock,
            metrics,
            NullLogger<ControlCycleUseCase>.Instance,
            ControlCycleOptions.Default);

        await cycle.ExecuteAsync("ghost", CancellationToken.None);

        Assert.Empty(metrics.InvalidSnapshots);
        Assert.Contains(metrics.SafeStops, e => e.AssetId == "ghost" && e.Reason == "asset-not-registered");
    }

    private static (ControlCycleUseCase Cycle, InMemorySnapshotStore Snapshots, SpyControlCycleMetrics Metrics, FakeClock Clock)
        Build(IDispatchOptimizer? optimizer = null)
    {
        var assets = new InMemoryBatteryAssetRegistry(new[] { TestFixtures.CreateAsset() });
        var snapshots = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        var metrics = new SpyControlCycleMetrics();
        var clock = new FakeClock();
        var cycle = new ControlCycleUseCase(
            assets,
            snapshots,
            new DefaultScheduleTracker(new InMemoryScheduleRepository()),
            new InMemoryOperatorStopRegistry(),
            optimizer ?? new NoOpDispatchOptimizer(),
            clock,
            metrics,
            NullLogger<ControlCycleUseCase>.Instance,
            ControlCycleOptions.Default);
        return (cycle, snapshots, metrics, clock);
    }

    private static ControlCycleUseCase BuildCycle(IOperatorStopRegistry stops, IControlCycleMetrics metrics)
    {
        var assets = new InMemoryBatteryAssetRegistry(new[] { TestFixtures.CreateAsset() });
        var snapshots = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);
        return new ControlCycleUseCase(
            assets,
            snapshots,
            new DefaultScheduleTracker(new InMemoryScheduleRepository()),
            stops,
            new NoOpDispatchOptimizer(),
            new FakeClock(),
            metrics,
            NullLogger<ControlCycleUseCase>.Instance,
            ControlCycleOptions.Default);
    }

    private sealed class FixedOptimizer(double targetPowerKw) : IDispatchOptimizer
    {
        public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new DispatchResult("fixed", targetPowerKw, "fixed-target", IsValid: true));
    }
}
