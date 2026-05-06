using BatteryEms.Adapters.Optimization;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Realtime;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

[Trait("Category", "Safety")]
public sealed class ControlCycleSafetyTests
{
    private static (ControlCycleUseCase Cycle, InMemorySnapshotStore Snapshots, FakeClock Clock, InMemoryBatteryAssetRegistry Assets)
        BuildCycle(IDispatchOptimizer? optimizer = null, IBatteryAssetRegistry? assets = null)
    {
        assets ??= new InMemoryBatteryAssetRegistry(new[] { TestFixtures.CreateAsset() });
        var snapshots = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        var clock = new FakeClock();
        var cycle = new ControlCycleUseCase(
            assets,
            snapshots,
            new DefaultScheduleTracker(new InMemoryScheduleRepository()),
            optimizer ?? new NoOpDispatchOptimizer(),
            clock,
            ControlCycleOptions.Default);
        return (cycle, snapshots, clock, (InMemoryBatteryAssetRegistry)assets);
    }

    [Fact]
    public async Task Unknown_asset_yields_safe_stop()
    {
        var (cycle, _, _, _) = BuildCycle(assets: new InMemoryBatteryAssetRegistry());

        var cmd = await cycle.ExecuteAsync("ghost-asset", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(0, cmd.ActivePowerKw);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Equal("asset-not-registered", cmd.Reason);
    }

    [Fact]
    public async Task Missing_snapshot_yields_safe_stop()
    {
        var (cycle, _, _, _) = BuildCycle();

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal("no-snapshot", cmd.Reason);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
    }

    [Fact]
    public async Task Stale_snapshot_yields_safe_stop()
    {
        var (cycle, snapshots, clock, _) = BuildCycle();
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);
        clock.UtcNow = TestFixtures.Now + TimeSpan.FromSeconds(30);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Contains("snapshot-aged", cmd.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Substituted_quality_yields_safe_stop()
    {
        var (cycle, snapshots, _, _) = BuildCycle();
        snapshots.Update(TestFixtures.CreateTelemetry(socPercent: 150), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Equal("soc-out-of-range", cmd.Reason);
    }

    [Fact]
    public async Task Protocol_error_quality_yields_safe_stop()
    {
        var (cycle, snapshots, _, _) = BuildCycle();
        snapshots.Update(TestFixtures.CreateTelemetry(activePowerKw: double.NaN), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Equal("active-power-not-finite", cmd.Reason);
    }

    [Fact]
    public async Task Unavailable_asset_yields_safe_stop()
    {
        var (cycle, snapshots, _, _) = BuildCycle();
        snapshots.Update(TestFixtures.CreateTelemetry(available: false), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Equal("asset-unavailable", cmd.Reason);
    }

    [Fact]
    public async Task Invalid_optimizer_result_yields_safe_stop()
    {
        var (cycle, snapshots, _, _) = BuildCycle(new BrokenOptimizer());
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(CommandSource.Fallback, cmd.Source);
        Assert.Equal("optimizer-failure", cmd.Reason);
    }

    [Fact]
    public async Task Soc_at_max_blocks_charge_request()
    {
        var (cycle, snapshots, _, _) = BuildCycle(new FixedOptimizer(-25));
        snapshots.Update(TestFixtures.CreateTelemetry(socPercent: 90), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Idle, cmd.Mode);
        Assert.Equal(0, cmd.ActivePowerKw);
        Assert.Equal("soc-at-max-charge-blocked", cmd.Reason);
    }

    [Fact]
    public async Task Soc_at_min_blocks_discharge_request()
    {
        var (cycle, snapshots, _, _) = BuildCycle(new FixedOptimizer(25));
        snapshots.Update(TestFixtures.CreateTelemetry(socPercent: 10), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Idle, cmd.Mode);
        Assert.Equal(0, cmd.ActivePowerKw);
        Assert.Equal("soc-at-min-discharge-blocked", cmd.Reason);
    }

    [Fact]
    public async Task Power_request_above_max_discharge_is_clamped()
    {
        var (cycle, snapshots, _, _) = BuildCycle(new FixedOptimizer(75));
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Discharge, cmd.Mode);
        Assert.Equal(50, cmd.ActivePowerKw);
        Assert.Equal("max-discharge-power", cmd.Reason);
    }

    [Fact]
    public async Task Power_request_below_neg_max_charge_is_clamped()
    {
        var (cycle, snapshots, _, _) = BuildCycle(new FixedOptimizer(-75));
        snapshots.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var cmd = await cycle.ExecuteAsync("asset-1", CancellationToken.None);

        Assert.Equal(CommandMode.Charge, cmd.Mode);
        Assert.Equal(-50, cmd.ActivePowerKw);
        Assert.Equal("max-charge-power", cmd.Reason);
    }

    private sealed class BrokenOptimizer : IDispatchOptimizer
    {
        public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(DispatchResult.Invalid("broken", "optimizer-failure"));
    }

    private sealed class FixedOptimizer(double targetPowerKw) : IDispatchOptimizer
    {
        public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new DispatchResult("fixed", targetPowerKw, "fixed-target", IsValid: true));
    }
}
